using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class PrevHighFirstPullbackBreakoutBacktest
    {
        public const string StrategyCode = "PREDAY_HIGH_FIRST_PULLBACK_BREAKOUT";
        public const string ExitRuleCode = "OBSERVE_180M_CLOSE";
        public const string OneMinutePrevLowCloseStopExitRuleCode = "ONE_MINUTE_PREV_LOW_CLOSE_STOP_OR_OBSERVE_180M";

        private const int Minute = 5;
        private const int ObservationMinutes = 180;
        private const int Ma5Period = 5;
        private const int Ma10Period = 10;
        private const long FiveMinuteTradingValueWon = 4_000_000_000; // 40eok, same money-flow gate as condition search.
        private const long ThreeMinuteAverageTradingValueWon = 3_000_000_000; // 30eok latest 3 completed 3m bars.

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public PrevHighFirstPullbackBreakoutBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run(
            bool useConditionSearchGate = false,
            decimal? maxEntryToPullbackRiskRate = null,
            bool useOneMinutePrevLowCloseStop = false,
            bool useNextDay1100MaxExit = false,
            bool useOneMinuteMa10ResetTwentyHighBreakReentry = false)
        {
            string maxExitName = useNextDay1100MaxExit ? "nextday1100" : "observe180";
            string baseRunName = useConditionSearchGate
                ? $"condition01_preday_high_first_pullback_breakout_5m_{maxExitName}"
                : $"preday_high_first_pullback_breakout_5m_{maxExitName}";
            if (useOneMinutePrevLowCloseStop)
                baseRunName += "_1m_prevlow_stop";
            if (useOneMinuteMa10ResetTwentyHighBreakReentry)
                baseRunName += "_reentry_ma10_reset_20high";

            string exitRuleCode = useOneMinutePrevLowCloseStop ? OneMinutePrevLowCloseStopExitRuleCode : ExitRuleCode;
            string runId = _runStore.CreateRunId(maxEntryToPullbackRiskRate.HasValue
                ? $"{baseRunName}_risk{maxEntryToPullbackRiskRate.Value:0.#}"
                : baseRunName);
            var signals = new List<BacktestSignalRow>();
            var trades = new List<BacktestTradeRow>();
            var reentryReviews = new List<ReentryReviewRequest>();
            int holdingBars = Math.Max(1, ObservationMinutes / Minute);

            foreach (StockMarketKey stock in EnumerateFiveMinuteStockMarkets())
            {
                List<BacktestMinuteBar> fiveBars = [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, Minute)
                    .OrderBy(item => item.DateTime)];
                List<BacktestMinuteBar> threeBars = useConditionSearchGate
                    ? [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, 3).OrderBy(item => item.DateTime)]
                    : [];
                List<BacktestMinuteBar> oneBars = useOneMinutePrevLowCloseStop
                    ? [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, 1).OrderBy(item => item.DateTime)]
                    : [];
                List<BacktestDailyBar> dailyBars = [.. _dataStore.LoadDailyBars(stock.Code, stock.Market)
                    .OrderBy(item => BacktestDataStore.NormalizeDate(item.Date))];

                if (fiveBars.Count < Ma10Period + holdingBars + 2 ||
                    dailyBars.Count < 22 ||
                    (useConditionSearchGate && threeBars.Count < 3) ||
                    (useOneMinutePrevLowCloseStop && oneBars.Count < 2))
                {
                    continue;
                }

                Dictionary<string, PreviousDayState> previousDayByDate = BuildPreviousDayMap(dailyBars);
                Dictionary<int, MaState> maByIndex = BuildMaStates(fiveBars);
                var usedDate = new HashSet<string>(StringComparer.Ordinal);

                string currentDate = string.Empty;
                StageState stage = StageState.None;
                long previousHigh = 0;
                long breakHigh = 0;
                long pullbackHigh = 0;
                long pullbackLow = 0;
                string breakTime = string.Empty;
                string pullbackTime = string.Empty;
                bool conditionGateSeen = !useConditionSearchGate;

                for (int i = 1; i < fiveBars.Count - holdingBars; i++)
                {
                    BacktestMinuteBar previous = fiveBars[i - 1];
                    BacktestMinuteBar current = fiveBars[i];
                    string date = ResolveDate(current.DateTime);
                    if (string.IsNullOrWhiteSpace(date))
                        continue;

                    if (!string.Equals(date, currentDate, StringComparison.Ordinal))
                    {
                        currentDate = date;
                        stage = StageState.None;
                        breakHigh = 0;
                        pullbackHigh = 0;
                        pullbackLow = 0;
                        breakTime = string.Empty;
                        pullbackTime = string.Empty;
                        conditionGateSeen = !useConditionSearchGate;
                        previousHigh = previousDayByDate.TryGetValue(date, out PreviousDayState previousDay)
                            ? previousDay.High
                            : 0;
                    }

                    if (previousHigh <= 0 || usedDate.Contains(date))
                        continue;

                    if (!maByIndex.TryGetValue(i, out MaState ma))
                        continue;

                    if (useConditionSearchGate && !conditionGateSeen)
                    {
                        if (!previousDayByDate.TryGetValue(date, out PreviousDayState dailyState) ||
                            !IsConditionSearchGateBar(dailyState, current, threeBars))
                        {
                            continue;
                        }

                        conditionGateSeen = true;
                        signals.Add(BuildSignal(runId, stock, current, "STAGE_CONDITION01_GATE",
                            $"Condition01Gate; previousHigh {previousHigh:N0}; BBUpper {CalculateIntradayBollingerUpper(dailyState.PreviousCloses, current.Close):N0}; 5mValue {current.TradingValue / 100_000_000m:0.##}eok"));
                    }

                    if (stage == StageState.None)
                    {
                        bool crossesAbovePreviousHigh =
                            (useConditionSearchGate || previous.Close <= previousHigh) &&
                            current.Close > previousHigh;

                        if (crossesAbovePreviousHigh)
                        {
                            stage = StageState.PrevHighBreak;
                            breakHigh = current.High;
                            breakTime = current.DateTime;
                            signals.Add(BuildSignal(runId, stock, current, "STAGE_PREV_HIGH_BREAK",
                                $"PrevHighBreak; previousHigh {previousHigh:N0}; breakHigh {breakHigh:N0}; value {current.TradingValue / 100_000_000m:0.##}eok"));
                        }

                        continue;
                    }

                    if (stage == StageState.PrevHighBreak)
                    {
                        bool ma5Support = current.Low <= ma.Ma5 && current.Close >= ma.Ma5;
                        bool ma10Support = current.Low <= ma.Ma10 && current.Close >= ma.Ma10;
                        if (ma5Support || ma10Support)
                        {
                            stage = StageState.FirstPullbackSupported;
                            pullbackHigh = current.High;
                            pullbackLow = current.Low;
                            pullbackTime = current.DateTime;
                            signals.Add(BuildSignal(runId, stock, current, "STAGE_FIRST_PULLBACK_SUPPORTED",
                                $"FirstPullbackSupported; previousHigh {previousHigh:N0}; breakHigh {breakHigh:N0}; pullbackHigh {pullbackHigh:N0}; pullbackLow {pullbackLow:N0}; MA5 {ma.Ma5:N0}; MA10 {ma.Ma10:N0}; support {(ma5Support ? "MA5" : "MA10")}; value {current.TradingValue / 100_000_000m:0.##}eok"));
                        }

                        continue;
                    }

                    if (stage == StageState.FirstPullbackSupported)
                    {
                        bool buySignal = current.Close > pullbackHigh && current.Close > previousHigh;
                        if (!buySignal)
                            continue;

                        List<BacktestMinuteBar> holding = useNextDay1100MaxExit
                            ? ResolveHoldingBarsToNextDay1100(fiveBars, i)
                            : ResolveHoldingBars(fiveBars, i, holdingBars);
                        if (holding.Count == 0)
                            continue;

                        long entryPrice = current.Close;
                        decimal entryToPullbackRiskRate = entryPrice > 0
                            ? (entryPrice - pullbackLow) / (decimal)entryPrice * 100m
                            : 0m;
                        if (maxEntryToPullbackRiskRate.HasValue && entryToPullbackRiskRate > maxEntryToPullbackRiskRate.Value)
                        {
                            signals.Add(BuildSignal(runId, stock, current, "SKIP_RISK_TOO_WIDE",
                                $"SkipRiskTooWide; previousHigh {previousHigh:N0}; pullbackLow {pullbackLow:N0}; entry {entryPrice:N0}; risk {entryToPullbackRiskRate:0.##}%; max {maxEntryToPullbackRiskRate.Value:0.##}%"));
                            stage = StageState.None;
                            usedDate.Add(date);
                            continue;
                        }

                        string plannedMaxExitTime = holding[^1].DateTime;
                        ExitState exit = ResolveExit(
                            holding,
                            oneBars,
                            current.DateTime,
                            useOneMinutePrevLowCloseStop,
                            useNextDay1100MaxExit);
                        long maxHigh = exit.MaxHigh;
                        long minLow = exit.MinLow;
                        decimal mfe = entryPrice > 0 ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m : 0m;
                        decimal mae = entryPrice > 0 ? (minLow - entryPrice) / (decimal)entryPrice * 100m : 0m;
                        decimal profitRate = entryPrice > 0 ? (exit.ExitPrice - entryPrice) / (decimal)entryPrice * 100m : 0m;
                        int holdingMinutes = ResolveHoldingMinutes(current.DateTime, exit.ExitTime, exit.HoldingMinutesFallback);
                        string reason =
                            $"BUY; PrevHighFirstPullbackBreakout; previousHigh {previousHigh:N0}; breakTime {breakTime}; breakHigh {breakHigh:N0}; pullbackTime {pullbackTime}; pullbackHigh {pullbackHigh:N0}; pullbackLow {pullbackLow:N0}; entry {entryPrice:N0}; value {current.TradingValue / 100_000_000m:0.##}eok";

                        signals.Add(BuildSignal(runId, stock, current, "BUY", reason));
                        trades.Add(new BacktestTradeRow
                        {
                            RunId = runId,
                            StrategyCode = StrategyCode,
                            ExitRuleCode = exitRuleCode,
                            Code = stock.Code,
                            Market = stock.Market,
                            EntryTime = current.DateTime,
                            ExitTime = exit.ExitTime,
                            EntryPrice = entryPrice,
                            ExitPrice = exit.ExitPrice,
                            MaxHigh = maxHigh,
                            MinLow = minLow,
                            StopPrice = pullbackLow,
                            Quantity = 1,
                            ProfitRate = profitRate,
                            ProfitAmount = exit.ExitPrice - entryPrice,
                            Mae = mae,
                            Mfe = mfe,
                            RiskRate = entryToPullbackRiskRate,
                            MaxR = 0,
                            MinR = 0,
                            HoldingMinutes = holdingMinutes,
                            EntryReason = reason,
                            ExitReason = $"{exit.ExitReason}; maxHigh MFE {mfe:0.##}%, minLow MAE {mae:0.##}%"
                        });

                        if (useOneMinuteMa10ResetTwentyHighBreakReentry &&
                            exit.StoppedByOneMinutePrevLow &&
                            TryResolveOneMinuteMa10ResetTwentyHighBreakReentry(oneBars, exit.ExitTime, plannedMaxExitTime, out ReentryTradeState reentry))
                        {
                            decimal reentryProfitRate = reentry.EntryPrice > 0
                                ? (reentry.ExitPrice - reentry.EntryPrice) / (decimal)reentry.EntryPrice * 100m
                                : 0m;
                            decimal reentryMfe = reentry.EntryPrice > 0
                                ? (reentry.MaxHigh - reentry.EntryPrice) / (decimal)reentry.EntryPrice * 100m
                                : 0m;
                            decimal reentryMae = reentry.EntryPrice > 0
                                ? (reentry.MinLow - reentry.EntryPrice) / (decimal)reentry.EntryPrice * 100m
                                : 0m;
                            string reentryReason =
                                $"REBUY; 1m MA10-or-lower reset then 20-bar high breakout after prev-low stop; firstExit {exit.ExitTime}; entry {reentry.EntryPrice:N0}; breakoutHigh {reentry.TriggerLine:N0}";

                            signals.Add(BuildSignal(runId, stock, reentry.EntryBar, "REBUY", reentryReason));
                            trades.Add(new BacktestTradeRow
                            {
                                RunId = runId,
                                StrategyCode = StrategyCode,
                                ExitRuleCode = exitRuleCode,
                                Code = stock.Code,
                                Market = stock.Market,
                                EntryTime = reentry.EntryTime,
                                ExitTime = reentry.ExitTime,
                                EntryPrice = reentry.EntryPrice,
                                ExitPrice = reentry.ExitPrice,
                                MaxHigh = reentry.MaxHigh,
                                MinLow = reentry.MinLow,
                                StopPrice = reentry.StopPrice,
                                Quantity = 1,
                                ProfitRate = reentryProfitRate,
                                ProfitAmount = reentry.ExitPrice - reentry.EntryPrice,
                                Mae = reentryMae,
                                Mfe = reentryMfe,
                                RiskRate = 0,
                                MaxR = 0,
                                MinR = 0,
                                HoldingMinutes = ResolveHoldingMinutes(reentry.EntryTime, reentry.ExitTime, reentry.HoldingMinutesFallback),
                                EntryReason = reentryReason,
                                ExitReason = $"{reentry.ExitReason}; maxHigh MFE {reentryMfe:0.##}%, minLow MAE {reentryMae:0.##}%"
                            });
                            reentryReviews.Add(new ReentryReviewRequest(
                                stock,
                                current.DateTime,
                                exit.ExitTime,
                                reentry.EntryTime,
                                reentry.ExitTime,
                                entryPrice,
                                exit.ExitPrice,
                                reentry.EntryPrice,
                                reentry.ExitPrice,
                                reentry.TriggerLine,
                                reentryProfitRate,
                                reentryMae,
                                reentryMfe));
                        }

                        usedDate.Add(date);
                    }
                }
            }

            List<BacktestRunSummary> summaries =
            [
                BuildSummary(runId, exitRuleCode, "MIXED", signals, trades),
                BuildSummary(runId, exitRuleCode, "KRX", signals.Where(item => item.Market == "KRX").ToList(), trades.Where(item => item.Market == "KRX").ToList()),
                BuildSummary(runId, exitRuleCode, "NXT", signals.Where(item => item.Market == "NXT").ToList(), trades.Where(item => item.Market == "NXT").ToList()),
                BuildSummary(runId, exitRuleCode, "AL", signals.Where(item => item.Market == "AL").ToList(), trades.Where(item => item.Market == "AL").ToList())
            ];

            var config = new BacktestRunConfig
            {
                RunId = runId,
                BacktestMode = "SOR_ON",
                RunMode = "SOR_ON",
                MarketMode = "MARKET_SPLIT",
                OrderMode = "None",
                LiveOrder = false,
                ExecutionType = "BacktestOnly",
                Memo = useConditionSearchGate
                    ? $"Condition search gate first: intraday daily BB upper break + previous high break + 5m 40eok + latest 3m avg 30eok. Then wait first MA5/MA10 pullback support and buy only when close breaks pullback high above previous high. {(maxEntryToPullbackRiskRate.HasValue ? $"Skip entries whose entry-to-pullback-low structural risk exceeds {maxEntryToPullbackRiskRate.Value:0.##}%." : "No structural risk cap.")} {(useOneMinutePrevLowCloseStop ? "Exit early when completed 1m candle closes below previous 1m low; otherwise hold until max exit." : "No early signal exit.")} {(useOneMinuteMa10ResetTwentyHighBreakReentry ? "After prev-low stop, keep tracking and allow one re-entry only after a 1m MA10-or-lower reset and 20-bar high close breakout." : "No re-entry.")} Max exit is {(useNextDay1100MaxExit ? "next trading day 11:00" : "180m observation close")}. No live orders."
                    : $"P=PREDAYHIGH; after close cross above P, wait first MA5/MA10 pullback support, then buy when close breaks pullback high and remains above P. {(maxEntryToPullbackRiskRate.HasValue ? $"Skip entries whose entry-to-pullback-low structural risk exceeds {maxEntryToPullbackRiskRate.Value:0.##}%." : "No structural risk cap.")} {(useOneMinutePrevLowCloseStop ? "Exit early when completed 1m candle closes below previous 1m low; otherwise hold until max exit." : "No early signal exit.")} {(useOneMinuteMa10ResetTwentyHighBreakReentry ? "After prev-low stop, keep tracking and allow one re-entry only after a 1m MA10-or-lower reset and 20-bar high close breakout." : "No re-entry.")} Max exit is {(useNextDay1100MaxExit ? "next trading day 11:00" : "180m observation close")}. No live orders."
            };

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries, config);
            if (useOneMinuteMa10ResetTwentyHighBreakReentry)
                SaveReentryReviewPacks(outputDirectory, reentryReviews);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries[0]);
        }

        private static BacktestSignalRow BuildSignal(string runId, StockMarketKey stock, BacktestMinuteBar bar, string type, string reason) =>
            new()
            {
                RunId = runId,
                StrategyCode = StrategyCode,
                Code = stock.Code,
                Market = stock.Market,
                SignalTime = bar.DateTime,
                SignalType = type,
                Price = bar.Close,
                Reason = reason
            };

        private IEnumerable<StockMarketKey> EnumerateFiveMinuteStockMarkets()
        {
            string directory = Path.Combine(_dataStore.RootPath, "minute", "5m");
            if (!Directory.Exists(directory))
                yield break;

            foreach (string path in Directory.EnumerateFiles(directory, "*_5m.json").OrderBy(item => item, StringComparer.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                string code = BacktestDataStore.NormalizeCode(parts[0]);
                string market = BacktestDataStore.NormalizeMarket(parts[1]);
                if (string.IsNullOrWhiteSpace(code) || market is not ("KRX" or "NXT" or "AL"))
                    continue;

                yield return new StockMarketKey(code, market);
            }
        }

        private static Dictionary<string, PreviousDayState> BuildPreviousDayMap(IReadOnlyList<BacktestDailyBar> dailyBars)
        {
            var result = new Dictionary<string, PreviousDayState>(StringComparer.Ordinal);
            List<BacktestDailyBar> sorted = [.. dailyBars
                .Where(item => !string.IsNullOrWhiteSpace(item.Date))
                .OrderBy(item => BacktestDataStore.NormalizeDate(item.Date))];

            for (int i = 20; i < sorted.Count; i++)
            {
                string date = BacktestDataStore.NormalizeDate(sorted[i].Date);
                BacktestDailyBar previous = sorted[i - 1];
                decimal previousUpper = CalculateBollingerUpper(sorted, i - 1, 20, 2m);
                List<decimal> previousCloses = [.. sorted
                    .Skip(i - 19)
                    .Take(19)
                    .Select(item => (decimal)item.Close)];
                result[date] = new PreviousDayState(previous.High, previous.Low, previous.Close, previousUpper, previousCloses);
            }

            return result;
        }

        private static bool IsConditionSearchGateBar(
            PreviousDayState dailyState,
            BacktestMinuteBar current,
            IReadOnlyList<BacktestMinuteBar> threeBars)
        {
            if (!IsMorningSearchWindow(current.DateTime) ||
                dailyState.High <= 0 ||
                current.Close <= dailyState.High ||
                current.TradingValue < FiveMinuteTradingValueWon ||
                !IsIntradayDailyBollingerUpperBreak(dailyState, current.Close) ||
                !TryGetLatestThreeMinuteAverageTradingValue(threeBars, current.DateTime, out decimal threeMinuteAverageTradingValue) ||
                threeMinuteAverageTradingValue < ThreeMinuteAverageTradingValueWon)
            {
                return false;
            }

            return true;
        }

        private static bool IsMorningSearchWindow(string dateTime)
        {
            if (!DateTime.TryParseExact(dateTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return false;

            TimeSpan time = parsed.TimeOfDay;
            return time >= TimeSpan.Parse("09:00:00", CultureInfo.InvariantCulture) &&
                   time <= TimeSpan.Parse("12:00:00", CultureInfo.InvariantCulture);
        }

        private static bool IsIntradayDailyBollingerUpperBreak(PreviousDayState dailyState, long currentPrice)
        {
            decimal upper = CalculateIntradayBollingerUpper(dailyState.PreviousCloses, currentPrice);
            return upper > 0m && currentPrice > upper && dailyState.Close <= dailyState.PreviousBollingerUpper;
        }

        private static decimal CalculateIntradayBollingerUpper(IReadOnlyList<decimal> previousCloses, long currentPrice)
        {
            if (previousCloses.Count < 19 || currentPrice <= 0)
                return 0m;

            List<decimal> closes = [.. previousCloses, currentPrice];
            decimal average = closes.Average();
            double variance = closes.Select(item => Math.Pow((double)(item - average), 2)).Average();
            decimal standardDeviation = (decimal)Math.Sqrt(variance);
            return average + standardDeviation * 2m;
        }

        private static decimal CalculateBollingerUpper(IReadOnlyList<BacktestDailyBar> bars, int index, int period, decimal width)
        {
            if (index < period - 1)
                return 0m;

            List<decimal> closes = [.. bars.Skip(index - period + 1).Take(period).Select(item => (decimal)item.Close)];
            if (closes.Count < period)
                return 0m;

            decimal average = closes.Average();
            double variance = closes.Select(item => Math.Pow((double)(item - average), 2)).Average();
            decimal standardDeviation = (decimal)Math.Sqrt(variance);
            return average + standardDeviation * width;
        }

        private static bool TryGetLatestThreeMinuteAverageTradingValue(
            IReadOnlyList<BacktestMinuteBar> threeBars,
            string baseTime,
            out decimal averageTradingValue)
        {
            averageTradingValue = 0m;
            List<BacktestMinuteBar> latest = [.. threeBars
                .Where(item => string.CompareOrdinal(item.DateTime, baseTime) <= 0)
                .OrderByDescending(item => item.DateTime)
                .Take(3)];

            if (latest.Count < 3)
                return false;

            averageTradingValue = latest.Average(item => (decimal)item.TradingValue);
            return true;
        }

        private static Dictionary<int, MaState> BuildMaStates(IReadOnlyList<BacktestMinuteBar> bars)
        {
            var result = new Dictionary<int, MaState>();
            for (int i = Ma10Period - 1; i < bars.Count; i++)
            {
                decimal ma5 = i >= Ma5Period - 1
                    ? bars.Skip(i - Ma5Period + 1).Take(Ma5Period).Average(item => (decimal)item.Close)
                    : 0m;
                decimal ma10 = bars.Skip(i - Ma10Period + 1).Take(Ma10Period).Average(item => (decimal)item.Close);
                result[i] = new MaState(ma5, ma10);
            }

            return result;
        }

        private static List<BacktestMinuteBar> ResolveHoldingBars(IReadOnlyList<BacktestMinuteBar> bars, int signalIndex, int count)
        {
            if (signalIndex < 0 || signalIndex >= bars.Count)
                return [];

            string signalDate = ResolveDate(bars[signalIndex].DateTime);
            var result = new List<BacktestMinuteBar>();
            for (int i = signalIndex + 1; i < bars.Count && result.Count < count; i++)
            {
                if (!string.Equals(ResolveDate(bars[i].DateTime), signalDate, StringComparison.Ordinal))
                    break;

                result.Add(bars[i]);
            }

            return result;
        }

        private static List<BacktestMinuteBar> ResolveHoldingBarsToNextDay1100(IReadOnlyList<BacktestMinuteBar> bars, int signalIndex)
        {
            if (signalIndex < 0 || signalIndex >= bars.Count)
                return [];

            string signalDate = ResolveDate(bars[signalIndex].DateTime);
            string nextDate = string.Empty;
            for (int i = signalIndex + 1; i < bars.Count; i++)
            {
                string date = ResolveDate(bars[i].DateTime);
                if (!string.IsNullOrWhiteSpace(date) && !string.Equals(date, signalDate, StringComparison.Ordinal))
                {
                    nextDate = date;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(nextDate))
                return ResolveHoldingBars(bars, signalIndex, Math.Max(1, ObservationMinutes / Minute));

            string maxExitTime = $"{nextDate}110000";
            var result = new List<BacktestMinuteBar>();
            for (int i = signalIndex + 1; i < bars.Count; i++)
            {
                if (string.CompareOrdinal(bars[i].DateTime, maxExitTime) > 0)
                    break;

                result.Add(bars[i]);
            }

            return result;
        }

        private static ExitState ResolveExit(
            IReadOnlyList<BacktestMinuteBar> holding,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            string entryTime,
            bool useOneMinutePrevLowCloseStop,
            bool useNextDay1100MaxExit)
        {
            BacktestMinuteBar plannedExit = holding[^1];
            long maxHigh = holding.Max(item => item.High);
            long minLow = holding.Min(item => item.Low);
            string maxExitReason = useNextDay1100MaxExit
                ? "max next trading day 11:00 close"
                : $"observe {ObservationMinutes}m close only";

            if (useOneMinutePrevLowCloseStop &&
                TryResolveOneMinutePrevLowCloseStop(oneBars, entryTime, plannedExit.DateTime, out BacktestMinuteBar stopBar, out BacktestMinuteBar previousBar))
            {
                List<BacktestMinuteBar> oneMinuteWindow = [.. oneBars
                    .Where(item => string.CompareOrdinal(item.DateTime, entryTime) > 0 &&
                                   string.CompareOrdinal(item.DateTime, stopBar.DateTime) <= 0)];
                if (oneMinuteWindow.Count > 0)
                {
                    maxHigh = oneMinuteWindow.Max(item => item.High);
                    minLow = oneMinuteWindow.Min(item => item.Low);
                }

                return new ExitState(
                    stopBar.DateTime,
                    stopBar.Close,
                    maxHigh,
                    minLow,
                    ResolveHoldingMinutes(entryTime, stopBar.DateTime, oneMinuteWindow.Count),
                    $"1m previous-low close stop; prevLow {previousBar.Low:N0}",
                    true);
            }

            return new ExitState(
                plannedExit.DateTime,
                plannedExit.Close,
                maxHigh,
                minLow,
                holding.Count * Minute,
                maxExitReason,
                false);
        }

        private static bool TryResolveOneMinutePrevLowCloseStop(
            IReadOnlyList<BacktestMinuteBar> oneBars,
            string entryTime,
            string maxExitTime,
            out BacktestMinuteBar stopBar,
            out BacktestMinuteBar previousBar)
        {
            stopBar = new BacktestMinuteBar();
            previousBar = new BacktestMinuteBar();
            for (int i = 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar previous = oneBars[i - 1];
                BacktestMinuteBar current = oneBars[i];
                if (string.CompareOrdinal(current.DateTime, entryTime) <= 0)
                    continue;

                if (string.CompareOrdinal(current.DateTime, maxExitTime) > 0)
                    break;

                if (current.Close < previous.Low)
                {
                    stopBar = current;
                    previousBar = previous;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveOneMinuteMa10ResetTwentyHighBreakReentry(
            IReadOnlyList<BacktestMinuteBar> oneBars,
            string trackingStartTime,
            string maxExitTime,
            out ReentryTradeState state)
        {
            state = default;
            bool ma10ResetSeen = false;
            for (int i = 20; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (string.CompareOrdinal(current.DateTime, trackingStartTime) <= 0)
                    continue;

                if (string.CompareOrdinal(current.DateTime, maxExitTime) > 0)
                    break;

                decimal currentMa10 = oneBars.Skip(i - 9).Take(10).Average(item => (decimal)item.Close);
                if (current.Close <= currentMa10)
                {
                    ma10ResetSeen = true;
                    continue;
                }

                long previousTwentyHigh = oneBars.Skip(i - 20).Take(20).Max(item => item.High);
                bool breakout = ma10ResetSeen &&
                    current.Close > previousTwentyHigh &&
                    current.Close > current.Open;
                if (!breakout)
                    continue;

                if (!TryResolveOneMinuteReentryExit(oneBars, i, maxExitTime, out OneMinuteReentryExitState exit))
                    return false;

                state = new ReentryTradeState(
                    current,
                    current.DateTime,
                    current.Close,
                    previousTwentyHigh,
                    exit.ExitTime,
                    exit.ExitPrice,
                    exit.MaxHigh,
                    exit.MinLow,
                    exit.StopPrice,
                    exit.HoldingMinutesFallback,
                    exit.ExitReason);
                return true;
            }

            return false;
        }

        private static bool TryResolveOneMinuteReentryExit(
            IReadOnlyList<BacktestMinuteBar> oneBars,
            int entryIndex,
            string maxExitTime,
            out OneMinuteReentryExitState state)
        {
            state = default;
            if (entryIndex < 0 || entryIndex >= oneBars.Count - 1)
                return false;

            long maxHigh = oneBars[entryIndex].High;
            long minLow = oneBars[entryIndex].Low;
            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar previous = oneBars[i - 1];
                BacktestMinuteBar current = oneBars[i];
                if (string.CompareOrdinal(current.DateTime, maxExitTime) > 0)
                    break;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);
                if (current.Close < previous.Low)
                {
                    state = new OneMinuteReentryExitState(
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        previous.Low,
                        i - entryIndex,
                        $"reentry 1m previous-low close stop; prevLow {previous.Low:N0}");
                    return true;
                }

                if (string.Equals(current.DateTime, maxExitTime, StringComparison.Ordinal))
                {
                    state = new OneMinuteReentryExitState(
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        previous.Low,
                        i - entryIndex,
                        "reentry max next trading day 11:00 close");
                    return true;
                }
            }

            BacktestMinuteBar? plannedExit = oneBars
                .Where(item => string.CompareOrdinal(item.DateTime, maxExitTime) <= 0)
                .LastOrDefault(item => string.CompareOrdinal(item.DateTime, oneBars[entryIndex].DateTime) > 0);
            if (plannedExit == null || string.IsNullOrWhiteSpace(plannedExit.DateTime))
                return false;

            state = new OneMinuteReentryExitState(
                plannedExit.DateTime,
                plannedExit.Close,
                maxHigh,
                minLow,
                oneBars[entryIndex].Low,
                1,
                "reentry max available close");
            return true;
        }

        private static string ResolveDate(string dateTime) =>
            dateTime.Length >= 8 ? dateTime[..8] : string.Empty;

        private static int ResolveHoldingMinutes(string entryTime, string exitTime, int fallback)
        {
            if (DateTime.TryParseExact(entryTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entry) &&
                DateTime.TryParseExact(exitTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exit))
            {
                return Math.Max(1, (int)Math.Round((exit - entry).TotalMinutes));
            }

            return Math.Max(1, fallback);
        }

        private static BacktestRunSummary BuildSummary(
            string runId,
            string exitRuleCode,
            string market,
            IReadOnlyList<BacktestSignalRow> signals,
            IReadOnlyList<BacktestTradeRow> trades)
        {
            int wins = trades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. trades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. trades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];

            return new BacktestRunSummary
            {
                RunId = runId,
                StrategyCode = StrategyCode,
                ExitRuleCode = exitRuleCode,
                Market = market,
                SignalCount = signals.Count,
                TradeCount = trades.Count,
                WinRate = trades.Count > 0 ? wins / (decimal)trades.Count * 100m : 0m,
                AvgProfit = profits.Count > 0 ? profits.Average() : 0m,
                AvgLoss = losses.Count > 0 ? losses.Average() : 0m,
                Expectancy = trades.Count > 0 ? trades.Average(trade => trade.ProfitRate) : 0m,
                TotalProfit = trades.Sum(trade => trade.ProfitAmount),
                MaxDrawdown = trades.Count > 0 ? trades.Min(trade => trade.Mae) : 0m,
                MAE = trades.Count > 0 ? trades.Average(trade => trade.Mae) : 0m,
                MFE = trades.Count > 0 ? trades.Average(trade => trade.Mfe) : 0m,
                AvgHoldingMinutes = trades.Count > 0 ? trades.Average(trade => (decimal)trade.HoldingMinutes) : 0m,
                ConsecutiveLosses = ResolveMaxConsecutiveLosses(trades),
                FeeAdjustedProfit = trades.Sum(trade => trade.ProfitAmount),
                SlippageAdjustedProfit = trades.Sum(trade => trade.ProfitAmount)
            };
        }

        private void SaveReentryReviewPacks(string outputDirectory, IReadOnlyList<ReentryReviewRequest> requests)
        {
            if (requests.Count == 0)
                return;

            string root = Path.Combine(outputDirectory, "reentry_review_packs");
            Directory.CreateDirectory(root);

            foreach (ReentryReviewRequest request in requests.Take(20))
            {
                string folderName = $"{request.Stock.Code}_{request.Stock.Market}_{request.ReentryTime}";
                string folder = Path.Combine(root, folderName);
                Directory.CreateDirectory(folder);

                File.WriteAllLines(Path.Combine(folder, "review-summary.txt"),
                [
                    "UTF-8",
                    $"Code: {request.Stock.Code}",
                    $"Market: {request.Stock.Market}",
                    $"FirstEntryTime: {request.FirstEntryTime}",
                    $"FirstStopTime: {request.FirstStopTime}",
                    $"ReentryTime: {request.ReentryTime}",
                    $"FinalExitTime: {request.FinalExitTime}",
                    $"FirstEntryPrice: {request.FirstEntryPrice:N0}",
                    $"FirstStopPrice: {request.FirstStopPrice:N0}",
                    $"ReentryPrice: {request.ReentryPrice:N0}",
                    $"FinalExitPrice: {request.FinalExitPrice:N0}",
                    $"TriggerLine: {request.TriggerLine:N0}",
                    $"ReentryProfitRate: {request.ReentryProfitRate:0.##}%",
                    $"ReentryMAE: {request.ReentryMae:0.##}%",
                    $"ReentryMFE: {request.ReentryMfe:0.##}%",
                    "VerdictTodo: DISCARD / WATCH / REBUY_CANDIDATE / REBUY_BLOCK",
                    "ReviewOrder: daily -> 30m -> 15m -> 5m -> 1m"
                ]);

                SaveDailyReviewChart(folder, request);
                SaveMinuteReviewChart(folder, request, 30, 180, 60);
                SaveMinuteReviewChart(folder, request, 15, 240, 80);
                SaveMinuteReviewChart(folder, request, 5, 260, 100);
                SaveMinuteReviewChart(folder, request, 1, 160, 80);
            }
        }

        private void SaveDailyReviewChart(string folder, ReentryReviewRequest request)
        {
            List<BacktestDailyBar> daily = [.. _dataStore.LoadDailyBars(request.Stock.Code, request.Stock.Market)
                .OrderBy(item => BacktestDataStore.NormalizeDate(item.Date))];
            if (daily.Count < 10)
                return;

            string firstDate = ResolveDate(request.FirstEntryTime);
            int center = daily.FindIndex(item => string.CompareOrdinal(BacktestDataStore.NormalizeDate(item.Date), firstDate) >= 0);
            if (center < 0)
                center = Math.Max(0, daily.Count - 1);

            int start = Math.Max(0, center - 140);
            int end = Math.Min(daily.Count - 1, center + 30);
            List<BacktestMinuteBar> bars = [.. daily.Skip(start).Take(end - start + 1).Select(item => new BacktestMinuteBar
            {
                Code = item.Code,
                Market = item.Market,
                Minute = 1440,
                DateTime = $"{BacktestDataStore.NormalizeDate(item.Date)}000000",
                Open = item.Open,
                High = item.High,
                Low = item.Low,
                Close = item.Close,
                Volume = item.Volume,
                TradingValue = item.TradingValue
            })];

            RenderReviewChart(
                Path.Combine(folder, "daily.png"),
                $"{request.Stock.Code} {request.Stock.Market} / DAILY",
                bars,
                request,
                [5, 20, 60, 120, 240],
                useDateOnlyMarkers: true);
        }

        private void SaveMinuteReviewChart(string folder, ReentryReviewRequest request, int minute, int before, int after)
        {
            List<BacktestMinuteBar> bars = [.. _dataStore.LoadMinuteBars(request.Stock.Code, request.Stock.Market, minute)
                .OrderBy(item => item.DateTime)];
            if (bars.Count < 10)
                return;

            int reentryIndex = FindTimeIndex(bars, request.ReentryTime);
            if (reentryIndex < 0)
                reentryIndex = FindTimeIndex(bars, request.FirstEntryTime);
            if (reentryIndex < 0)
                return;

            int start = Math.Max(0, reentryIndex - before);
            int end = Math.Min(bars.Count - 1, reentryIndex + after);
            List<BacktestMinuteBar> window = [.. bars.Skip(start).Take(end - start + 1)];
            if (window.Count < 10)
                return;

            RenderReviewChart(
                Path.Combine(folder, $"{minute}m.png"),
                $"{request.Stock.Code} {request.Stock.Market} / {minute}m reentry review",
                window,
                request,
                minute == 1 ? [10, 20, 60] : [5, 10, 20, 60],
                useDateOnlyMarkers: false);
        }

        private static void RenderReviewChart(
            string path,
            string title,
            IReadOnlyList<BacktestMinuteBar> bars,
            ReentryReviewRequest request,
            IReadOnlyList<int> maPeriods,
            bool useDateOnlyMarkers)
        {
            const int width = 1320;
            const int height = 760;
            const int left = 62;
            const int right = 42;
            const int top = 58;
            const int priceBottom = 520;
            const int volumeTop = 552;
            const int bottom = 724;

            long high = bars.Max(bar => bar.High);
            long low = bars.Min(bar => bar.Low);
            long maxVolume = Math.Max(1, bars.Max(bar => bar.Volume));
            double xStep = (width - left - right) / (double)Math.Max(1, bars.Count - 1);

            double PriceY(long price)
            {
                if (high == low)
                    return (top + priceBottom) / 2.0;
                return priceBottom - (price - low) / (double)(high - low) * (priceBottom - top);
            }

            double VolumeY(long volume) =>
                bottom - volume / (double)maxVolume * (bottom - volumeTop);

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 12, 12)), null, new Rect(0, 0, width, height));
                var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(42, 42, 42)), 1);
                for (int i = 0; i <= 5; i++)
                {
                    double y = top + i * (priceBottom - top) / 5.0;
                    dc.DrawLine(gridPen, new Point(left, y), new Point(width - right, y));
                    long labelPrice = high - (long)((high - low) * i / 5.0);
                    DrawText(dc, labelPrice.ToString("N0", CultureInfo.InvariantCulture), width - right - 4, y - 8, 10, Color.FromRgb(180, 185, 195));
                }
                dc.DrawLine(gridPen, new Point(left, volumeTop), new Point(width - right, volumeTop));
                dc.DrawLine(gridPen, new Point(left, bottom), new Point(width - right, bottom));

                DrawText(dc, title, 18, 16, 15, Colors.White);
                DrawText(dc, $"FIRST {request.FirstEntryTime}  STOP {request.FirstStopTime}  REBUY {request.ReentryTime}  EXIT {request.FinalExitTime}", 18, 36, 12, Color.FromRgb(185, 198, 215));

                Color[] maColors =
                [
                    Color.FromRgb(245, 85, 190),
                    Color.FromRgb(130, 160, 255),
                    Color.FromRgb(255, 210, 50),
                    Color.FromRgb(75, 215, 80),
                    Color.FromRgb(180, 180, 180)
                ];
                for (int i = 0; i < maPeriods.Count; i++)
                {
                    List<double?> ma = CalculateMa(bars, maPeriods[i]);
                    DrawMa(dc, bars, ma, left, xStep, PriceY, maColors[Math.Min(i, maColors.Length - 1)]);
                    DrawText(dc, $"MA{maPeriods[i]}", 18 + i * 52, 54, 10, maColors[Math.Min(i, maColors.Length - 1)]);
                }

                DrawHorizontalPrice(dc, left, width - right, PriceY, low, high, request.FirstEntryPrice, "FIRST BUY", Color.FromRgb(80, 220, 120));
                DrawHorizontalPrice(dc, left, width - right, PriceY, low, high, request.ReentryPrice, "REBUY", Color.FromRgb(80, 225, 255));
                DrawHorizontalPrice(dc, left, width - right, PriceY, low, high, (long)Math.Round(request.TriggerLine), "20H", Color.FromRgb(255, 235, 80));

                for (int i = 0; i < bars.Count; i++)
                {
                    BacktestMinuteBar bar = bars[i];
                    double x = left + i * xStep;
                    bool up = bar.Close >= bar.Open;
                    Color color = up ? Color.FromRgb(255, 72, 72) : Color.FromRgb(70, 145, 255);
                    var brush = new SolidColorBrush(color);
                    var pen = new Pen(brush, 1.1);
                    dc.DrawLine(pen, new Point(x, PriceY(bar.High)), new Point(x, PriceY(bar.Low)));
                    dc.DrawRectangle(brush, null, new Rect(x - 3, Math.Min(PriceY(bar.Open), PriceY(bar.Close)), 6, Math.Max(2, Math.Abs(PriceY(bar.Open) - PriceY(bar.Close)))));
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)), null, new Rect(x - 3, VolumeY(bar.Volume), 6, bottom - VolumeY(bar.Volume)));

                    DrawTimeMarker(dc, bar, x, top, priceBottom, request.FirstEntryTime, "FIRST", Color.FromRgb(80, 220, 120), useDateOnlyMarkers);
                    DrawTimeMarker(dc, bar, x, top, priceBottom, request.FirstStopTime, "STOP", Color.FromRgb(255, 105, 70), useDateOnlyMarkers);
                    DrawTimeMarker(dc, bar, x, top, priceBottom, request.ReentryTime, "REBUY", Color.FromRgb(80, 225, 255), useDateOnlyMarkers);
                    DrawTimeMarker(dc, bar, x, top, priceBottom, request.FinalExitTime, "EXIT", Color.FromRgb(255, 170, 90), useDateOnlyMarkers);
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void DrawHorizontalPrice(DrawingContext dc, int left, int right, Func<long, double> priceY, long low, long high, long price, string label, Color color)
        {
            if (price <= 0 || price < low || price > high)
                return;

            double y = priceY(price);
            var pen = new Pen(new SolidColorBrush(color), 1.2) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(left, y), new Point(right, y));
            DrawText(dc, $"{label} {price:N0}", right - 150, y - 14, 10, color);
        }

        private static void DrawTimeMarker(DrawingContext dc, BacktestMinuteBar bar, double x, int top, int bottom, string targetTime, string label, Color color, bool useDateOnly)
        {
            bool match = useDateOnly
                ? ResolveDate(bar.DateTime) == ResolveDate(targetTime)
                : string.Equals(bar.DateTime, targetTime, StringComparison.Ordinal);
            if (!match)
                return;

            var pen = new Pen(new SolidColorBrush(color), 1.8);
            dc.DrawLine(pen, new Point(x, top), new Point(x, bottom));
            DrawText(dc, label, x + 4, top + 8, 10, color);
        }

        private static void DrawMa(
            DrawingContext dc,
            IReadOnlyList<BacktestMinuteBar> bars,
            IReadOnlyList<double?> ma,
            int left,
            double xStep,
            Func<long, double> priceY,
            Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 1.4);
            Point? previous = null;
            for (int i = 0; i < bars.Count; i++)
            {
                if (!ma[i].HasValue)
                    continue;

                var current = new Point(left + i * xStep, priceY((long)ma[i]!.Value));
                if (previous.HasValue)
                    dc.DrawLine(pen, previous.Value, current);
                previous = current;
            }
        }

        private static List<double?> CalculateMa(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new List<double?>(bars.Count);
            for (int i = 0; i < bars.Count; i++)
            {
                if (i < period - 1)
                {
                    result.Add(null);
                    continue;
                }

                result.Add((double)bars.Skip(i - period + 1).Take(period).Average(bar => (decimal)bar.Close));
            }

            return result;
        }

        private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                size,
                new SolidColorBrush(color),
                1.0);
            dc.DrawText(formatted, new Point(x, y));
        }

        private static int FindTimeIndex(IReadOnlyList<BacktestMinuteBar> bars, string dateTime)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                if (string.CompareOrdinal(bars[i].DateTime, dateTime) >= 0)
                    return i;
            }

            return -1;
        }

        private static int ResolveMaxConsecutiveLosses(IEnumerable<BacktestTradeRow> trades)
        {
            int max = 0;
            int current = 0;
            foreach (BacktestTradeRow trade in trades)
            {
                if (trade.ProfitRate <= 0)
                {
                    current++;
                    max = Math.Max(max, current);
                }
                else
                {
                    current = 0;
                }
            }

            return max;
        }

        private enum StageState
        {
            None,
            PrevHighBreak,
            FirstPullbackSupported
        }

        private sealed record StockMarketKey(string Code, string Market);
        private sealed record ReentryReviewRequest(
            StockMarketKey Stock,
            string FirstEntryTime,
            string FirstStopTime,
            string ReentryTime,
            string FinalExitTime,
            long FirstEntryPrice,
            long FirstStopPrice,
            long ReentryPrice,
            long FinalExitPrice,
            decimal TriggerLine,
            decimal ReentryProfitRate,
            decimal ReentryMae,
            decimal ReentryMfe);
        private readonly record struct PreviousDayState(long High, long Low, long Close, decimal PreviousBollingerUpper, IReadOnlyList<decimal> PreviousCloses);
        private readonly record struct MaState(decimal Ma5, decimal Ma10);
        private readonly record struct ExitState(string ExitTime, long ExitPrice, long MaxHigh, long MinLow, int HoldingMinutesFallback, string ExitReason, bool StoppedByOneMinutePrevLow);
        private readonly record struct ReentryTradeState(
            BacktestMinuteBar EntryBar,
            string EntryTime,
            long EntryPrice,
            decimal TriggerLine,
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            long StopPrice,
            int HoldingMinutesFallback,
            string ExitReason);
        private readonly record struct OneMinuteReentryExitState(
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            long StopPrice,
            int HoldingMinutesFallback,
            string ExitReason);
    }
}
