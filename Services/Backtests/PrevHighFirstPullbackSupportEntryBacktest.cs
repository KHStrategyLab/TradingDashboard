using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class PrevHighFirstPullbackSupportEntryBacktest
    {
        public const string StrategyCode = "PREDAY_HIGH_FIRST_PULLBACK_SUPPORT_ENTRY";
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

        public PrevHighFirstPullbackSupportEntryBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run(
            bool useConditionSearchGate = false,
            bool useOneMinutePrevLowCloseStop = false,
            bool useNextDay1100MaxExit = false)
        {
            string maxExitName = useNextDay1100MaxExit ? "nextday1100" : "observe180";
            string baseRunName = useConditionSearchGate
                ? $"condition01_preday_high_first_pullback_support_entry_5m_{maxExitName}"
                : $"preday_high_first_pullback_support_entry_5m_{maxExitName}";
            if (useOneMinutePrevLowCloseStop)
                baseRunName += "_1m_prevlow_stop";

            string exitRuleCode = useOneMinutePrevLowCloseStop ? OneMinutePrevLowCloseStopExitRuleCode : ExitRuleCode;
            string runId = _runStore.CreateRunId(baseRunName);
            var signals = new List<BacktestSignalRow>();
            var trades = new List<BacktestTradeRow>();
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
                bool prevHighBroken = false;
                long previousHigh = 0;
                long breakHigh = 0;
                string breakTime = string.Empty;
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
                        prevHighBroken = false;
                        breakHigh = 0;
                        breakTime = string.Empty;
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

                    if (!prevHighBroken)
                    {
                        bool crossesAbovePreviousHigh =
                            (useConditionSearchGate || previous.Close <= previousHigh) &&
                            current.Close > previousHigh;

                        if (crossesAbovePreviousHigh)
                        {
                            prevHighBroken = true;
                            breakHigh = current.High;
                            breakTime = current.DateTime;
                            signals.Add(BuildSignal(runId, stock, current, "STAGE_PREV_HIGH_BREAK",
                                $"PrevHighBreak; previousHigh {previousHigh:N0}; breakHigh {breakHigh:N0}; value {current.TradingValue / 100_000_000m:0.##}eok"));
                        }

                        continue;
                    }

                    bool ma5Support = current.Low <= ma.Ma5 && current.Close >= ma.Ma5;
                    bool ma10Support = current.Low <= ma.Ma10 && current.Close >= ma.Ma10;
                    bool stillAbovePreviousHigh = current.Close > previousHigh;
                    if (!(ma5Support || ma10Support) || !stillAbovePreviousHigh)
                        continue;

                    List<BacktestMinuteBar> holding = useNextDay1100MaxExit
                        ? ResolveHoldingBarsToNextDay1100(fiveBars, i)
                        : ResolveHoldingBars(fiveBars, i, holdingBars);
                    if (holding.Count == 0)
                        continue;

                    long entryPrice = current.Close;
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
                    string reason =
                        $"BUY; PrevHighFirstPullbackSupportEntry; previousHigh {previousHigh:N0}; breakTime {breakTime}; breakHigh {breakHigh:N0}; pullbackHigh {current.High:N0}; pullbackLow {current.Low:N0}; MA5 {ma.Ma5:N0}; MA10 {ma.Ma10:N0}; support {(ma5Support ? "MA5" : "MA10")}; entry {entryPrice:N0}; value {current.TradingValue / 100_000_000m:0.##}eok";

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
                        StopPrice = current.Low,
                        Quantity = 1,
                        ProfitRate = profitRate,
                        ProfitAmount = exit.ExitPrice - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        RiskRate = entryPrice > 0 ? (entryPrice - current.Low) / (decimal)entryPrice * 100m : 0m,
                        MaxR = 0,
                        MinR = 0,
                        HoldingMinutes = ResolveHoldingMinutes(current.DateTime, exit.ExitTime, exit.HoldingMinutesFallback),
                        EntryReason = reason,
                        ExitReason = $"{exit.ExitReason}; maxHigh MFE {mfe:0.##}%, minLow MAE {mae:0.##}%"
                    });

                    usedDate.Add(date);
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
                    ? $"Condition search gate first: intraday daily BB upper break + previous high break + 5m 40eok + latest 3m avg 30eok. Then buy first MA5/MA10 pullback support candle. {(useOneMinutePrevLowCloseStop ? "Exit early when completed 1m candle closes below previous 1m low; otherwise hold until max exit." : "No early signal exit.")} Max exit is {(useNextDay1100MaxExit ? "next trading day 11:00" : "180m observation close")}. No live orders."
                    : $"After close crosses above previous day high, buy the first MA5/MA10 pullback support candle that still closes above previous high. {(useOneMinutePrevLowCloseStop ? "Exit early when completed 1m candle closes below previous 1m low; otherwise hold until max exit." : "No early signal exit.")} Max exit is {(useNextDay1100MaxExit ? "next trading day 11:00" : "180m observation close")}. No live orders."
            };

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries, config);
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
                    $"1m previous-low close stop; prevLow {previousBar.Low:N0}");
            }

            return new ExitState(
                plannedExit.DateTime,
                plannedExit.Close,
                maxHigh,
                minLow,
                holding.Count * Minute,
                maxExitReason);
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

        private sealed record StockMarketKey(string Code, string Market);
        private readonly record struct PreviousDayState(long High, long Low, long Close, decimal PreviousBollingerUpper, IReadOnlyList<decimal> PreviousCloses);
        private readonly record struct MaState(decimal Ma5, decimal Ma10);
        private readonly record struct ExitState(string ExitTime, long ExitPrice, long MaxHigh, long MinLow, int HoldingMinutesFallback, string ExitReason);
    }
}
