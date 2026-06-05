using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class TenMinutePullbackFiveMinuteBreakoutSignalExitBacktest
    {
        public const string StrategyCode = "TEN_MA60_PULLBACK_FIVE_HIGH20_BREAK";
        public const string ExitRuleCode = "SIGNAL_EXIT_1M_MA5_5M_BASE_15M_STRUCTURE_MAX180";

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public TenMinutePullbackFiveMinuteBreakoutSignalExitBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run(int maxHoldingMinutes = 180)
        {
            int resolvedMaxHoldingMinutes = Math.Max(5, maxHoldingMinutes);
            string runId = _runStore.CreateRunId($"ten_ma60_pullback_five_high20_signal_exit{resolvedMaxHoldingMinutes}");
            List<BacktestSignalRow> signals = [];
            List<BacktestTradeRow> trades = [];

            List<BaseGroup> groups = [.. _dataStore.LoadBaseCandles()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.BaseCandleDate))
                .GroupBy(item => $"{BacktestDataStore.NormalizeCode(item.Code)}|{BacktestDataStore.NormalizeMarket(item.Market)}", StringComparer.Ordinal)
                .Select(group => new BaseGroup(
                    BacktestDataStore.NormalizeCode(group.First().Code),
                    BacktestDataStore.NormalizeMarket(group.First().Market),
                    group.Min(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate)) ?? DateTime.Today.ToString("yyyyMMdd")))
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Market)];

            foreach (BaseGroup group in groups)
            {
                List<BacktestMinuteBar> oneBars = LoadBars(group, 1);
                List<BacktestMinuteBar> fiveBars = LoadBars(group, 5);
                List<BacktestMinuteBar> tenBars = LoadBars(group, 10);
                List<BacktestMinuteBar> fifteenBars = LoadBars(group, 15);

                if (oneBars.Count < 25 || fiveBars.Count < 25 || tenBars.Count < 61 || fifteenBars.Count < 6)
                    continue;

                Dictionary<string, TenState> tenStateByTime = BuildTenStateMap(tenBars);
                Dictionary<string, MinuteState> oneStateByTime = BuildMinuteStateMap(oneBars, 5);
                Dictionary<string, MinuteState> fiveStateByTime = BuildMinuteStateMap(fiveBars, 5);
                Dictionary<string, MinuteState> fifteenStateByTime = BuildMinuteStateMap(fifteenBars, 5);

                int skipUntilFiveIndex = -1;
                for (int i = 20; i < fiveBars.Count - 1; i++)
                {
                    if (i <= skipUntilFiveIndex)
                        continue;

                    BacktestMinuteBar entryBar = fiveBars[i];
                    if (!TryGetLatestTenState(tenStateByTime, entryBar.DateTime, out TenState tenState) ||
                        !tenState.WasDownCrossed ||
                        tenState.Close >= tenState.Ma60)
                    {
                        continue;
                    }

                    List<BacktestMinuteBar> previous20 = fiveBars.GetRange(i - 20, 20);
                    BacktestMinuteBar highCloseBar = previous20
                        .OrderByDescending(bar => bar.Close)
                        .ThenByDescending(bar => bar.DateTime)
                        .First();

                    if (!IsBreakoutSignal(entryBar, highCloseBar))
                        continue;

                    if (!TryResolveSignalExit(
                        group,
                        entryBar,
                        highCloseBar,
                        oneBars,
                        oneStateByTime,
                        fiveStateByTime,
                        fifteenStateByTime,
                        resolvedMaxHoldingMinutes,
                        out SignalExitResult exit))
                    {
                        continue;
                    }

                    long entryPrice = entryBar.Close;
                    decimal profitRate = entryPrice > 0
                        ? (exit.ExitPrice - entryPrice) / (decimal)entryPrice * 100m
                        : 0m;
                    decimal mae = entryPrice > 0
                        ? (exit.MinLow - entryPrice) / (decimal)entryPrice * 100m
                        : 0m;
                    decimal mfe = entryPrice > 0
                        ? (exit.MaxHigh - entryPrice) / (decimal)entryPrice * 100m
                        : 0m;

                    string entryReason = $"10m MA60 below after down-cross; 5m open inside high20 candle {highCloseBar.Low}-{highCloseBar.High}; close>{highCloseBar.Close}; vol {entryBar.Volume}>{highCloseBar.Volume}; signal-exit test";
                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = group.Code,
                        Market = group.Market,
                        SignalTime = entryBar.DateTime,
                        SignalType = "BUY",
                        Price = entryPrice,
                        Reason = entryReason
                    });

                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = group.Code,
                        Market = group.Market,
                        SignalTime = exit.ExitTime,
                        SignalType = "SELL",
                        Price = exit.ExitPrice,
                        Reason = exit.ExitReason
                    });

                    trades.Add(new BacktestTradeRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        ExitRuleCode = ExitRuleCode,
                        Code = group.Code,
                        Market = group.Market,
                        EntryTime = entryBar.DateTime,
                        ExitTime = exit.ExitTime,
                        EntryPrice = entryPrice,
                        ExitPrice = exit.ExitPrice,
                        MaxHigh = exit.MaxHigh,
                        MinLow = exit.MinLow,
                        StopPrice = highCloseBar.Low,
                        Quantity = 1,
                        ProfitRate = profitRate,
                        ProfitAmount = exit.ExitPrice - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        HoldingMinutes = exit.HoldingMinutes,
                        EntryReason = entryReason,
                        ExitReason = exit.ExitReason
                    });

                    skipUntilFiveIndex = i;
                    while (skipUntilFiveIndex + 1 < fiveBars.Count &&
                           string.CompareOrdinal(fiveBars[skipUntilFiveIndex + 1].DateTime, exit.ExitTime) <= 0)
                    {
                        skipUntilFiveIndex++;
                    }
                }
            }

            List<BacktestRunSummary> summaries =
            [
                BuildSummary(runId, "MIXED", signals, trades),
                BuildSummary(runId, "KRX", signals.Where(item => item.Market == "KRX").ToList(), trades.Where(item => item.Market == "KRX").ToList()),
                BuildSummary(runId, "NXT", signals.Where(item => item.Market == "NXT").ToList(), trades.Where(item => item.Market == "NXT").ToList())
            ];

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries[0]);
        }

        private List<BacktestMinuteBar> LoadBars(BaseGroup group, int minute)
        {
            return [.. _dataStore.LoadMinuteBars(group.Code, group.Market, minute)
                .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                .OrderBy(bar => bar.DateTime)];
        }

        private static bool TryResolveSignalExit(
            BaseGroup group,
            BacktestMinuteBar entryBar,
            BacktestMinuteBar highCloseBar,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyDictionary<string, MinuteState> oneStateByTime,
            IReadOnlyDictionary<string, MinuteState> fiveStateByTime,
            IReadOnlyDictionary<string, MinuteState> fifteenStateByTime,
            int maxHoldingMinutes,
            out SignalExitResult result)
        {
            result = default;
            int entryIndex = FindTimeIndex(oneBars, entryBar.DateTime);
            if (entryIndex < 0 || entryIndex + 1 >= oneBars.Count)
                return false;

            string entryDate = entryBar.DateTime.Length >= 8 ? entryBar.DateTime[..8] : string.Empty;
            long entryPrice = entryBar.Close;
            long maxHigh = entryBar.High;
            long minLow = entryBar.Low;
            long baseLow = highCloseBar.Low;

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                string currentDate = current.DateTime.Length >= 8 ? current.DateTime[..8] : string.Empty;
                if (!string.Equals(entryDate, currentDate, StringComparison.Ordinal))
                    break;

                int holdingMinutes = ResolveHoldingMinutes(entryBar.DateTime, current.DateTime, i - entryIndex);
                if (holdingMinutes <= 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                decimal profitRate = entryPrice > 0 ? (current.Close - entryPrice) / (decimal)entryPrice * 100m : 0m;

                bool oneWeak = TryGetLatestMinuteState(oneStateByTime, current.DateTime, out MinuteState oneState) &&
                    oneState.PreviousClose >= oneState.PreviousMa5 &&
                    oneState.Close < oneState.Ma5;
                bool fiveWeak = TryGetLatestMinuteState(fiveStateByTime, current.DateTime, out MinuteState fiveState) &&
                    fiveState.Close < fiveState.Ma5;
                bool fifteenWeak = TryGetLatestMinuteState(fifteenStateByTime, current.DateTime, out MinuteState fifteenState) &&
                    fifteenState.Close < fifteenState.Ma5;
                bool baseLowBroken = baseLow > 0 && current.Close <= baseLow;

                string? exitReason = ResolveExitReason(
                    group.Market,
                    current,
                    profitRate,
                    oneWeak,
                    fiveWeak,
                    fifteenWeak,
                    baseLowBroken,
                    maxHoldingMinutes,
                    holdingMinutes);

                if (string.IsNullOrWhiteSpace(exitReason))
                    continue;

                result = new SignalExitResult(
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    holdingMinutes,
                    exitReason);
                return true;
            }

            return false;
        }

        private static string? ResolveExitReason(
            string market,
            BacktestMinuteBar current,
            decimal profitRate,
            bool oneWeak,
            bool fiveWeak,
            bool fifteenWeak,
            bool baseLowBroken,
            int maxHoldingMinutes,
            int holdingMinutes)
        {
            if (baseLowBroken)
                return $"5m high20/base low broken at {current.Close:N0}";

            if (fifteenWeak)
                return "15m MA5 flow damaged";

            if (fiveWeak)
                return $"5m MA5 structural weakness, pnl {profitRate:0.##}%";

            if (oneWeak)
                return $"1m MA5 down-cross, pnl {profitRate:0.##}%";

            if (holdingMinutes >= maxHoldingMinutes)
                return $"max holding {maxHoldingMinutes}m reached ({market})";

            return null;
        }

        private static bool IsBreakoutSignal(BacktestMinuteBar current, BacktestMinuteBar highCloseBar)
        {
            if (current.Close <= current.Open)
                return false;
            if (current.Close <= highCloseBar.Close)
                return false;
            if (current.Open < highCloseBar.Low || current.Open > highCloseBar.High)
                return false;
            if (current.Open >= highCloseBar.Close * 1.01m)
                return false;
            return current.Volume > highCloseBar.Volume;
        }

        private static Dictionary<string, TenState> BuildTenStateMap(IReadOnlyList<BacktestMinuteBar> tenBars)
        {
            var result = new Dictionary<string, TenState>(StringComparer.Ordinal);
            bool wasDownCrossed = false;

            for (int i = 59; i < tenBars.Count; i++)
            {
                decimal ma60 = tenBars.Skip(i - 59).Take(60).Average(bar => (decimal)bar.Close);
                bool downCrossed = false;
                if (i > 59)
                {
                    TenState previous = result[tenBars[i - 1].DateTime];
                    downCrossed = tenBars[i - 1].Close >= previous.Ma60 && tenBars[i].Close < ma60;
                }

                wasDownCrossed = wasDownCrossed || downCrossed;
                result[tenBars[i].DateTime] = new TenState(tenBars[i].DateTime, tenBars[i].Close, ma60, wasDownCrossed);
            }

            return result;
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

        private static bool TryGetLatestTenState(
            Dictionary<string, TenState> states,
            string fiveTime,
            out TenState state)
        {
            state = default;
            string? key = states.Keys
                .Where(time => string.CompareOrdinal(time, fiveTime) <= 0)
                .OrderBy(time => time)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            state = states[key];
            return true;
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

        private static BacktestRunSummary BuildSummary(string runId, string market, IReadOnlyList<BacktestSignalRow> signals, IReadOnlyList<BacktestTradeRow> trades)
        {
            int wins = trades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. trades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. trades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];

            return new BacktestRunSummary
            {
                RunId = runId,
                Market = market,
                StrategyCode = StrategyCode,
                ExitRuleCode = ExitRuleCode,
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

        private static bool IsAfterBase(string dateTime, string baseDate)
        {
            string threshold = $"{BacktestDataStore.NormalizeDate(baseDate)}000000";
            return string.CompareOrdinal(dateTime, threshold) >= 0;
        }

        private sealed record BaseGroup(string Code, string Market, string BaseDate);
        private readonly record struct TenState(string Time, long Close, decimal Ma60, bool WasDownCrossed);
        private readonly record struct MinuteState(string Time, long Close, decimal Ma5, long PreviousClose, decimal PreviousMa5);
        private readonly record struct SignalExitResult(string ExitTime, long ExitPrice, long MaxHigh, long MinLow, int HoldingMinutes, string ExitReason);
    }
}
