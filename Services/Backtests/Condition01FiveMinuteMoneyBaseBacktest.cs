using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class Condition01FiveMinuteMoneyBaseBacktest
    {
        public const string StrategyCode = "CONDITION01_5M_MONEY_BASE_1M_TRIGGER";
        public const string ExitRuleCode = "STRUCTURAL_1M_MA5_5M_BASE_LOW_15M_MAX180";
        public const string CloseExitRuleCode = "SAME_DAY_CLOSE_EXIT";

        private const long FiveMinuteTradingValueWon = 4_000_000_000; // 40억, Kiwoom minute trading value is stored as won.
        private const long ThreeMinuteAverageTradingValueWon = 3_000_000_000; // 30억, latest 3 completed 3m bars.
        private const int MaxHoldingMinutes = 180;

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public Condition01FiveMinuteMoneyBaseBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run(bool useCloseExit = false)
        {
            string runId = _runStore.CreateRunId(useCloseExit
                ? "condition01_5m_money_base_1m_close_exit"
                : "condition01_5m_money_base_1m_trigger");
            string exitRuleCode = useCloseExit ? CloseExitRuleCode : ExitRuleCode;
            List<BacktestSignalRow> signals = [];
            List<BacktestTradeRow> trades = [];

            foreach (StockMarketKey stock in EnumerateFiveMinuteStockMarkets())
            {
                List<BacktestDailyBar> dailyBars = [.. _dataStore.LoadDailyBars(stock.Code, stock.Market)
                    .OrderBy(item => BacktestDataStore.NormalizeDate(item.Date))];
                List<BacktestMinuteBar> oneBars = LoadMinuteBars(stock, 1);
                List<BacktestMinuteBar> threeBars = LoadMinuteBars(stock, 3);
                List<BacktestMinuteBar> fiveBars = LoadMinuteBars(stock, 5);
                List<BacktestMinuteBar> fifteenBars = LoadMinuteBars(stock, 15);

                if (dailyBars.Count < 22 || oneBars.Count < 25 || threeBars.Count < 3 || fiveBars.Count < 22 || fifteenBars.Count < 6)
                    continue;

                Dictionary<string, DailyConditionState> dailyStateByDate = BuildDailyConditionStateMap(dailyBars);
                Dictionary<string, MinuteState> oneStateByTime = BuildMinuteStateMap(oneBars, 5);
                Dictionary<string, MinuteState> fifteenStateByTime = BuildMinuteStateMap(fifteenBars, 5);
                HashSet<string> usedDate = new(StringComparer.Ordinal);

                for (int i = 20; i < fiveBars.Count; i++)
                {
                    BacktestMinuteBar baseBar = fiveBars[i];
                    string date = ResolveDate(baseBar.DateTime);
                    if (string.IsNullOrWhiteSpace(date) || usedDate.Contains(date))
                        continue;

                    if (!IsMorningSearchWindow(baseBar.DateTime))
                        continue;

                    if (!dailyStateByDate.TryGetValue(date, out DailyConditionState dailyState) ||
                        !IsIntradayDailyBollingerUpperBreak(dailyState, baseBar.Close) ||
                        dailyState.PreviousHigh <= 0 ||
                        baseBar.Close <= dailyState.PreviousHigh)
                    {
                        continue;
                    }

                    if (baseBar.TradingValue < FiveMinuteTradingValueWon ||
                        !TryGetLatestThreeMinuteAverageTradingValue(threeBars, baseBar.DateTime, out decimal threeMinuteAverageTradingValue) ||
                        threeMinuteAverageTradingValue < ThreeMinuteAverageTradingValueWon)
                    {
                        continue;
                    }

                    BaseState baseState = new(
                        baseBar.DateTime,
                        baseBar.Open,
                        baseBar.High,
                        baseBar.Low,
                        baseBar.Close,
                        baseBar.TradingValue,
                        (baseBar.High + baseBar.Low) / 2m,
                        dailyState.PreviousHigh,
                        CalculateIntradayBollingerUpper(dailyState.PreviousCloses, baseBar.Close),
                        threeMinuteAverageTradingValue);

                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = stock.Code,
                        Market = stock.Market,
                        SignalTime = baseBar.DateTime,
                        SignalType = "BASE",
                        Price = baseBar.Close,
                        Reason = BuildBaseReason(baseState)
                    });

                    if (TryFindOneMinuteEntryAndExit(
                        stock,
                        baseState,
                        oneBars,
                        oneStateByTime,
                        fifteenStateByTime,
                        useCloseExit,
                        out EntryExitResult trade))
                    {
                        signals.Add(new BacktestSignalRow
                        {
                            RunId = runId,
                            StrategyCode = StrategyCode,
                            Code = stock.Code,
                            Market = stock.Market,
                            SignalTime = trade.EntryTime,
                            SignalType = "BUY",
                            Price = trade.EntryPrice,
                            Reason = trade.EntryReason
                        });

                        signals.Add(new BacktestSignalRow
                        {
                            RunId = runId,
                            StrategyCode = StrategyCode,
                            Code = stock.Code,
                            Market = stock.Market,
                            SignalTime = trade.ExitTime,
                            SignalType = "SELL",
                            Price = trade.ExitPrice,
                            Reason = trade.ExitReason
                        });

                        trades.Add(BuildTradeRow(runId, stock, trade));
                    }

                    usedDate.Add(date);
                }
            }

            List<BacktestRunSummary> summaries =
            [
                BuildSummary(runId, exitRuleCode, "MIXED", signals, trades),
                BuildSummary(runId, exitRuleCode, "KRX", signals.Where(item => item.Market == "KRX").ToList(), trades.Where(item => item.Market == "KRX").ToList()),
                BuildSummary(runId, exitRuleCode, "NXT", signals.Where(item => item.Market == "NXT").ToList(), trades.Where(item => item.Market == "NXT").ToList())
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
                Memo = useCloseExit
                    ? "Condition 01 style 5m money-base scan with same-day close exit. DataStore is read-only; results are saved under Runs only."
                    : "Condition 01 style 5m money-base scan. DataStore is read-only; results are saved under Runs only."
            };

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries, config);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries[0]);
        }

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
                if (string.IsNullOrWhiteSpace(code) || market is not ("KRX" or "NXT"))
                    continue;

                yield return new StockMarketKey(code, market);
            }
        }

        private List<BacktestMinuteBar> LoadMinuteBars(StockMarketKey stock, int minute) =>
            [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, minute)
                .OrderBy(item => item.DateTime)];

        private static Dictionary<string, DailyConditionState> BuildDailyConditionStateMap(IReadOnlyList<BacktestDailyBar> bars)
        {
            var result = new Dictionary<string, DailyConditionState>(StringComparer.Ordinal);
            for (int i = 20; i < bars.Count; i++)
            {
                BacktestDailyBar previous = bars[i - 1];
                BacktestDailyBar current = bars[i];
                decimal previousUpper = CalculateBollingerUpper(bars, i - 1, 20, 2m);
                List<decimal> previousCloses = [.. bars
                    .Skip(i - 19)
                    .Take(19)
                    .Select(item => (decimal)item.Close)];

                result[BacktestDataStore.NormalizeDate(current.Date)] = new DailyConditionState(
                    BacktestDataStore.NormalizeDate(current.Date),
                    previous.High,
                    previous.Close,
                    previousUpper,
                    previousCloses);
            }

            return result;
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

        private static bool IsIntradayDailyBollingerUpperBreak(DailyConditionState dailyState, long currentPrice)
        {
            decimal upper = CalculateIntradayBollingerUpper(dailyState.PreviousCloses, currentPrice);
            return upper > 0m && currentPrice > upper && dailyState.PreviousClose <= dailyState.PreviousBollingerUpper;
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

            averageTradingValue = latest.Average(item => (decimal)Math.Max(0, item.TradingValue));
            return true;
        }

        private static bool TryFindOneMinuteEntryAndExit(
            StockMarketKey stock,
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyDictionary<string, MinuteState> oneStateByTime,
            IReadOnlyDictionary<string, MinuteState> fifteenStateByTime,
            bool useCloseExit,
            out EntryExitResult result)
        {
            result = default;
            int baseIndex = FindTimeIndex(oneBars, baseState.Time);
            if (baseIndex < 20 || baseIndex + 1 >= oneBars.Count)
                return false;

            string baseDate = ResolveDate(baseState.Time);
            bool supportSeen = false;
            for (int i = baseIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (!string.Equals(baseDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                int elapsed = ResolveHoldingMinutes(baseState.Time, current.DateTime, i - baseIndex);
                if (elapsed > MaxHoldingMinutes)
                    break;

                if (!supportSeen)
                {
                    supportSeen = current.Low <= baseState.Close && current.Close >= baseState.Center;
                    continue;
                }

                BacktestMinuteBar previous = oneBars[i - 1];
                List<BacktestMinuteBar> previous20 = [.. oneBars.Skip(i - 20).Take(20)];
                decimal averageVolume20 = previous20.Average(item => (decimal)Math.Max(0, item.Volume));
                bool bullish = current.Close > current.Open;
                bool previousHighBreak = current.Close > previous.High;
                bool ma5Recover = TryGetLatestMinuteState(oneStateByTime, current.DateTime, out MinuteState oneState) &&
                    current.Close >= oneState.Ma5;
                bool volumeOk = averageVolume20 <= 0 || current.Volume >= averageVolume20 * 1.2m;

                if (!bullish || !previousHighBreak || !ma5Recover || !volumeOk)
                    continue;

                long entryPrice = current.Close;
                bool exitResolved = useCloseExit
                    ? TryResolveSameDayCloseExit(current, oneBars, i, out ExitResult exit)
                    : TryResolveSignalExit(
                        current,
                        baseState,
                        oneBars,
                        i,
                        oneStateByTime,
                        fifteenStateByTime,
                        out exit);

                if (!exitResolved)
                {
                    return false;
                }

                result = new EntryExitResult(
                    current.DateTime,
                    entryPrice,
                    exit.ExitTime,
                    exit.ExitPrice,
                    exit.MaxHigh,
                    exit.MinLow,
                    baseState.Low,
                    exit.HoldingMinutes,
                    BuildEntryReason(baseState),
                    exit.ExitReason);
                return true;
            }

            return false;
        }

        private static bool TryResolveSameDayCloseExit(
            BacktestMinuteBar entryBar,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            int entryIndex,
            out ExitResult result)
        {
            result = default;
            string entryDate = ResolveDate(entryBar.DateTime);
            string closeCutoff = ResolveCloseCutoff(entryBar.Market, entryDate);
            List<BacktestMinuteBar> sameDayAfterEntry = [];

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (!string.Equals(entryDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                if (string.CompareOrdinal(current.DateTime, closeCutoff) > 0)
                    break;

                sameDayAfterEntry.Add(current);
            }

            if (sameDayAfterEntry.Count == 0)
                return false;

            BacktestMinuteBar closeBar = sameDayAfterEntry[^1];
            result = new ExitResult(
                closeBar.DateTime,
                closeBar.Close,
                Math.Max(entryBar.High, sameDayAfterEntry.Max(item => item.High)),
                Math.Min(entryBar.Low, sameDayAfterEntry.Min(item => item.Low)),
                ResolveHoldingMinutes(entryBar.DateTime, closeBar.DateTime, sameDayAfterEntry.Count),
                "same-day close exit");
            return true;
        }

        private static bool TryResolveSignalExit(
            BacktestMinuteBar entryBar,
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            int entryIndex,
            IReadOnlyDictionary<string, MinuteState> oneStateByTime,
            IReadOnlyDictionary<string, MinuteState> fifteenStateByTime,
            out ExitResult result)
        {
            result = default;
            string entryDate = ResolveDate(entryBar.DateTime);
            long maxHigh = entryBar.High;
            long minLow = entryBar.Low;

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (!string.Equals(entryDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                int holdingMinutes = ResolveHoldingMinutes(entryBar.DateTime, current.DateTime, i - entryIndex);
                if (holdingMinutes <= 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                bool baseLowBroken = baseState.Low > 0 && current.Close <= baseState.Low;
                bool oneWeak = TryGetLatestMinuteState(oneStateByTime, current.DateTime, out MinuteState oneState) &&
                    oneState.PreviousClose >= oneState.PreviousMa5 &&
                    oneState.Close < oneState.Ma5;
                bool fifteenWeak = TryGetLatestMinuteState(fifteenStateByTime, current.DateTime, out MinuteState fifteenState) &&
                    fifteenState.Close < fifteenState.Ma5;

                string? reason = null;
                if (baseLowBroken)
                    reason = $"5m money-base low broken at {current.Close:N0}";
                else if (fifteenWeak)
                    reason = "15m MA5 flow damaged";
                else if (oneWeak)
                    reason = "1m MA5 down-cross";
                else if (holdingMinutes >= MaxHoldingMinutes)
                    reason = $"max holding {MaxHoldingMinutes}m reached";

                if (string.IsNullOrWhiteSpace(reason))
                    continue;

                result = new ExitResult(current.DateTime, current.Close, maxHigh, minLow, holdingMinutes, reason);
                return true;
            }

            return false;
        }

        private static Dictionary<string, MinuteState> BuildMinuteStateMap(IReadOnlyList<BacktestMinuteBar> bars, int maPeriod)
        {
            var result = new Dictionary<string, MinuteState>(StringComparer.Ordinal);
            int period = Math.Max(2, maPeriod);
            for (int i = period - 1; i < bars.Count; i++)
            {
                BacktestMinuteBar current = bars[i];
                decimal ma = bars.Skip(i - period + 1).Take(period).Average(bar => (decimal)bar.Close);
                long previousClose = i > 0 ? bars[i - 1].Close : current.Close;
                decimal previousMa = i > period - 1
                    ? bars.Skip(i - period).Take(period).Average(bar => (decimal)bar.Close)
                    : ma;

                result[current.DateTime] = new MinuteState(current.DateTime, current.Close, ma, previousClose, previousMa);
            }

            return result;
        }

        private static bool TryGetLatestMinuteState(
            IReadOnlyDictionary<string, MinuteState> states,
            string time,
            out MinuteState state)
        {
            state = default;
            string? key = states.Keys
                .Where(item => string.CompareOrdinal(item, time) <= 0)
                .OrderBy(item => item)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            state = states[key];
            return state.Ma5 > 0;
        }

        private static BacktestTradeRow BuildTradeRow(string runId, StockMarketKey stock, EntryExitResult trade)
        {
            long riskWon = trade.EntryPrice - trade.StopPrice;
            decimal profitRate = trade.EntryPrice > 0
                ? (trade.ExitPrice - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal mae = trade.EntryPrice > 0
                ? (trade.MinLow - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal mfe = trade.EntryPrice > 0
                ? (trade.MaxHigh - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal riskRate = trade.EntryPrice > 0 && riskWon > 0
                ? riskWon / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal maxR = riskWon > 0 ? (trade.MaxHigh - trade.EntryPrice) / (decimal)riskWon : 0m;
            decimal minR = riskWon > 0 ? (trade.MinLow - trade.EntryPrice) / (decimal)riskWon : 0m;

            return new BacktestTradeRow
            {
                RunId = runId,
                StrategyCode = StrategyCode,
                ExitRuleCode = trade.ExitReason == "same-day close exit" ? CloseExitRuleCode : ExitRuleCode,
                Code = stock.Code,
                Market = stock.Market,
                EntryTime = trade.EntryTime,
                ExitTime = trade.ExitTime,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                MaxHigh = trade.MaxHigh,
                MinLow = trade.MinLow,
                StopPrice = trade.StopPrice,
                Quantity = 1,
                ProfitRate = profitRate,
                ProfitAmount = trade.ExitPrice - trade.EntryPrice,
                Mae = mae,
                Mfe = mfe,
                RiskRate = riskRate,
                MaxR = maxR,
                MinR = minR,
                HoldingMinutes = trade.HoldingMinutes,
                EntryReason = trade.EntryReason,
                ExitReason = trade.ExitReason
            };
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
                Market = market,
                StrategyCode = StrategyCode,
                ExitRuleCode = exitRuleCode,
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

        private static bool IsMorningSearchWindow(string dateTime)
        {
            string time = ResolveTime(dateTime);
            return string.CompareOrdinal(time, "090000") >= 0 && string.CompareOrdinal(time, "120000") <= 0;
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

        private static int ResolveHoldingMinutes(string entryTime, string exitTime, int fallbackBars)
        {
            if (DateTime.TryParseExact(entryTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entry) &&
                DateTime.TryParseExact(exitTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exit))
            {
                return Math.Max(1, (int)Math.Round((exit - entry).TotalMinutes));
            }

            return Math.Max(1, fallbackBars);
        }

        private static string ResolveDate(string dateTime)
        {
            string digits = new([.. (dateTime ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 8 ? digits[..8] : string.Empty;
        }

        private static string ResolveTime(string dateTime)
        {
            string digits = new([.. (dateTime ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 14 ? digits.Substring(8, 6) : string.Empty;
        }

        private static string ResolveCloseCutoff(string market, string date)
        {
            string normalizedMarket = BacktestDataStore.NormalizeMarket(market);
            string time = normalizedMarket == "NXT" ? "200000" : "153000";
            return $"{date}{time}";
        }

        private static string BuildBaseReason(BaseState state) =>
            $"Condition01 5m money-base: 5m value {state.TradingValue / 100_000_000m:0.##}억 >=40억; 3m avg {state.ThreeMinuteAverageTradingValue / 100_000_000m:0.##}억 >=30억; close>{state.PreviousDayHigh:N0} prev-high; daily BB upper {state.BollingerUpper:N0} break";

        private static string BuildEntryReason(BaseState state) =>
            $"1m pullback/rebreak after 5m money-base {state.Time}; support above center {state.Center:N0}; trigger close above previous high with MA5/volume confirmation";

        private readonly record struct StockMarketKey(string Code, string Market);
        private readonly record struct DailyConditionState(
            string Date,
            long PreviousHigh,
            long PreviousClose,
            decimal PreviousBollingerUpper,
            IReadOnlyList<decimal> PreviousCloses);
        private readonly record struct BaseState(
            string Time,
            long Open,
            long High,
            long Low,
            long Close,
            long TradingValue,
            decimal Center,
            long PreviousDayHigh,
            decimal BollingerUpper,
            decimal ThreeMinuteAverageTradingValue);
        private readonly record struct MinuteState(
            string Time,
            long Close,
            decimal Ma5,
            long PreviousClose,
            decimal PreviousMa5);
        private readonly record struct ExitResult(
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            int HoldingMinutes,
            string ExitReason);
        private readonly record struct EntryExitResult(
            string EntryTime,
            long EntryPrice,
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            long StopPrice,
            int HoldingMinutes,
            string EntryReason,
            string ExitReason);
    }
}
