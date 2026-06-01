using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class TenMinutePullbackFiveMinuteBreakoutBacktest
    {
        public const string StrategyCode = "TEN_MA60_PULLBACK_FIVE_HIGH20_BREAK";
        private const string ExitRuleCode = "HOLD_30M_HIGH_LOW";
        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public TenMinutePullbackFiveMinuteBreakoutBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run()
        {
            string runId = _runStore.CreateRunId("ten_ma60_pullback_five_high20_hold30");
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
                List<BacktestMinuteBar> tenBars = [.. _dataStore.LoadMinuteBars(group.Code, group.Market, 10)
                    .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                    .OrderBy(bar => bar.DateTime)];
                List<BacktestMinuteBar> fiveBars = [.. _dataStore.LoadMinuteBars(group.Code, group.Market, 5)
                    .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                    .OrderBy(bar => bar.DateTime)];

                if (tenBars.Count < 61 || fiveBars.Count < 27)
                    continue;

                Dictionary<string, TenState> tenStateByTime = BuildTenStateMap(tenBars);
                int skipUntilIndex = -1;

                for (int i = 20; i < fiveBars.Count - 6; i++)
                {
                    if (i <= skipUntilIndex)
                        continue;

                    BacktestMinuteBar current = fiveBars[i];
                    if (!TryGetLatestTenState(tenStateByTime, current.DateTime, out TenState tenState) ||
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

                    if (!IsBreakoutSignal(current, highCloseBar))
                        continue;

                    List<BacktestMinuteBar> holdingBars = ResolveHoldingBars(fiveBars, i, 6);
                    if (holdingBars.Count < 6)
                        continue;

                    long maxHigh = holdingBars.Max(bar => bar.High);
                    long minLow = holdingBars.Min(bar => bar.Low);
                    BacktestMinuteBar exitBar = holdingBars.Last();
                    long entryPrice = current.Close;
                    decimal mfe = entryPrice > 0 ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    decimal mae = entryPrice > 0 ? (minLow - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    decimal exitRate = entryPrice > 0 ? (exitBar.Close - entryPrice) / (decimal)entryPrice * 100m : 0m;

                    string reason = $"10m MA60 below after down-cross; 5m close>{highCloseBar.Close} high20 close; vol {current.Volume}>{highCloseBar.Volume}; gap<{1m:0.##}%";
                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = group.Code,
                        Market = group.Market,
                        SignalTime = current.DateTime,
                        SignalType = "BUY",
                        Price = entryPrice,
                        Reason = reason
                    });

                    trades.Add(new BacktestTradeRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        ExitRuleCode = ExitRuleCode,
                        Code = group.Code,
                        Market = group.Market,
                        EntryTime = current.DateTime,
                        ExitTime = exitBar.DateTime,
                        EntryPrice = entryPrice,
                        ExitPrice = exitBar.Close,
                        MaxHigh = maxHigh,
                        MinLow = minLow,
                        Quantity = 1,
                        ProfitRate = exitRate,
                        ProfitAmount = exitBar.Close - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        HoldingMinutes = 30,
                        EntryReason = reason,
                        ExitReason = "30-minute observation window"
                    });

                    skipUntilIndex = i + 6;
                }
            }

            BacktestRunSummary summary = BuildSummary(runId, signals, trades);
            string outputDirectory = _runStore.SaveRun(runId, signals, trades, [summary]);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summary);
        }

        private static bool IsBreakoutSignal(BacktestMinuteBar current, BacktestMinuteBar highCloseBar)
        {
            if (current.Close <= current.Open)
                return false;
            if (current.Close <= highCloseBar.Close)
                return false;
            if (current.Open > highCloseBar.Close)
                return false;
            if (current.Open >= highCloseBar.Close * 1.01m)
                return false;
            return current.Volume > highCloseBar.Volume;
        }

        private static List<BacktestMinuteBar> ResolveHoldingBars(IReadOnlyList<BacktestMinuteBar> bars, int signalIndex, int count)
        {
            if (signalIndex < 0 || signalIndex >= bars.Count)
                return [];

            string signalDate = bars[signalIndex].DateTime.Length >= 8 ? bars[signalIndex].DateTime[..8] : string.Empty;
            var result = new List<BacktestMinuteBar>();
            for (int i = signalIndex + 1; i < bars.Count && result.Count < count; i++)
            {
                string date = bars[i].DateTime.Length >= 8 ? bars[i].DateTime[..8] : string.Empty;
                if (!string.Equals(date, signalDate, StringComparison.Ordinal))
                    break;

                result.Add(bars[i]);
            }

            return result;
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

        private static BacktestRunSummary BuildSummary(string runId, IReadOnlyList<BacktestSignalRow> signals, IReadOnlyList<BacktestTradeRow> trades)
        {
            int wins = trades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. trades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. trades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];

            return new BacktestRunSummary
            {
                RunId = runId,
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
    }

    public sealed record BacktestRunResult(
        string RunId,
        string OutputDirectory,
        int SignalCount,
        int TradeCount,
        BacktestRunSummary Summary);
}
