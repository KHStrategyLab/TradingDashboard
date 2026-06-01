using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class SmallBaseCenterPullbackBacktest
    {
        public const string StrategyCode = "SMALL_BASE_MA60_CENTER_PULLBACK";
        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public SmallBaseCenterPullbackBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run(
            int baseMinute = 10,
            int entryMinute = 3,
            int observationMinutes = 180,
            decimal baseRisePercent = 1.0m,
            long baseTradingValueWon = 1_000_000_000,
            decimal entryVolumeMultiplier = 1.2m)
        {
            int holdingBars = Math.Max(1, observationMinutes / Math.Max(1, entryMinute));
            string exitRuleCode = $"OBSERVE_{observationMinutes}M_R";
            string runId = _runStore.CreateRunId($"small_base_center_{baseMinute}m_{entryMinute}m_hold{observationMinutes}");
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
                List<BacktestMinuteBar> baseBars = [.. _dataStore.LoadMinuteBars(group.Code, group.Market, baseMinute)
                    .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                    .OrderBy(bar => bar.DateTime)];
                List<BacktestMinuteBar> entryBars = [.. _dataStore.LoadMinuteBars(group.Code, group.Market, entryMinute)
                    .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                    .OrderBy(bar => bar.DateTime)];

                if (baseBars.Count < 61 || entryBars.Count < 22 + holdingBars)
                    continue;

                Dictionary<string, BaseState> baseStates = BuildBaseStateMap(baseBars, baseRisePercent, baseTradingValueWon);
                var usedBaseTimes = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 21; i < entryBars.Count - holdingBars; i++)
                {
                    BacktestMinuteBar entryBar = entryBars[i];
                    if (!TryGetLatestBaseState(baseStates, entryBar.DateTime, out BaseState baseState) ||
                        !baseState.IsSmallBase ||
                        usedBaseTimes.Contains(baseState.Time))
                    {
                        continue;
                    }

                    BacktestMinuteBar previousEntry = entryBars[i - 1];
                    List<BacktestMinuteBar> previous20 = entryBars.GetRange(i - 20, 20);
                    decimal avgVolume20 = previous20.Average(bar => (decimal)Math.Max(0, bar.Volume));
                    if (!IsCenterPullbackEntry(entryBar, previousEntry, baseState, avgVolume20, entryVolumeMultiplier))
                        continue;

                    List<BacktestMinuteBar> holdingWindow = ResolveHoldingBars(entryBars, i, holdingBars);
                    if (holdingWindow.Count < holdingBars)
                        continue;

                    long entryPrice = entryBar.Close;
                    long stopPrice = baseState.Low;
                    long riskWon = entryPrice - stopPrice;
                    if (entryPrice <= 0 || riskWon <= 0)
                        continue;

                    long maxHigh = holdingWindow.Max(bar => bar.High);
                    long minLow = holdingWindow.Min(bar => bar.Low);
                    BacktestMinuteBar exitBar = holdingWindow.Last();
                    decimal mfe = (maxHigh - entryPrice) / (decimal)entryPrice * 100m;
                    decimal mae = (minLow - entryPrice) / (decimal)entryPrice * 100m;
                    decimal exitRate = (exitBar.Close - entryPrice) / (decimal)entryPrice * 100m;
                    decimal riskRate = riskWon / (decimal)entryPrice * 100m;
                    decimal maxR = (maxHigh - entryPrice) / (decimal)riskWon;
                    decimal minR = (minLow - entryPrice) / (decimal)riskWon;

                    string reason = $"{baseMinute}m small-base MA60 recover {baseState.Time}; center={baseState.Center:0}; {entryMinute}m bullish rebound above prev high; vol>{entryVolumeMultiplier:0.##}x avg20";
                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = group.Code,
                        Market = group.Market,
                        SignalTime = entryBar.DateTime,
                        SignalType = "BUY",
                        Price = entryPrice,
                        Reason = reason
                    });

                    trades.Add(new BacktestTradeRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        ExitRuleCode = exitRuleCode,
                        Code = group.Code,
                        Market = group.Market,
                        EntryTime = entryBar.DateTime,
                        ExitTime = exitBar.DateTime,
                        EntryPrice = entryPrice,
                        ExitPrice = exitBar.Close,
                        MaxHigh = maxHigh,
                        MinLow = minLow,
                        StopPrice = stopPrice,
                        Quantity = 1,
                        ProfitRate = exitRate,
                        ProfitAmount = exitBar.Close - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        RiskRate = riskRate,
                        MaxR = maxR,
                        MinR = minR,
                        HoldingMinutes = observationMinutes,
                        EntryReason = reason,
                        ExitReason = $"{observationMinutes}-minute signal-quality window"
                    });

                    usedBaseTimes.Add(baseState.Time);
                }
            }

            BacktestRunSummary summary = BuildSummary(runId, exitRuleCode, signals, trades);
            string outputDirectory = _runStore.SaveRun(runId, signals, trades, [summary]);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summary);
        }

        private static Dictionary<string, BaseState> BuildBaseStateMap(
            IReadOnlyList<BacktestMinuteBar> bars,
            decimal baseRisePercent,
            long baseTradingValueWon)
        {
            var result = new Dictionary<string, BaseState>(StringComparer.Ordinal);

            for (int i = 60; i < bars.Count; i++)
            {
                BacktestMinuteBar previous = bars[i - 1];
                BacktestMinuteBar current = bars[i];
                decimal previousMa60 = bars.Skip(i - 60).Take(60).Average(bar => (decimal)bar.Close);
                decimal currentMa60 = bars.Skip(i - 59).Take(60).Average(bar => (decimal)bar.Close);
                decimal rise = current.Open > 0 ? (current.Close - current.Open) / (decimal)current.Open * 100m : 0m;
                bool isSmallBase =
                    previous.Close < previousMa60 &&
                    current.Low < currentMa60 &&
                    current.Close > currentMa60 &&
                    current.Close > current.Open &&
                    rise >= baseRisePercent &&
                    current.TradingValue >= baseTradingValueWon;

                result[current.DateTime] = new BaseState(
                    current.DateTime,
                    current.Open,
                    current.High,
                    current.Low,
                    current.Close,
                    current.TradingValue,
                    currentMa60,
                    (current.High + current.Low) / 2m,
                    isSmallBase);
            }

            return result;
        }

        private static bool IsCenterPullbackEntry(
            BacktestMinuteBar entryBar,
            BacktestMinuteBar previousEntry,
            BaseState baseState,
            decimal avgVolume20,
            decimal entryVolumeMultiplier)
        {
            bool centerSupport = entryBar.Close >= baseState.Center && entryBar.Close <= baseState.Close;
            bool rebound = entryBar.Close > entryBar.Open && entryBar.Close > previousEntry.High;
            bool volumeOk = avgVolume20 <= 0 || entryBar.Volume >= avgVolume20 * entryVolumeMultiplier;
            return centerSupport && rebound && volumeOk;
        }

        private static bool TryGetLatestBaseState(Dictionary<string, BaseState> states, string entryTime, out BaseState state)
        {
            state = default;
            string? key = states.Keys
                .Where(time => string.CompareOrdinal(time, entryTime) < 0)
                .OrderBy(time => time)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            state = states[key];
            return true;
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

        private static BacktestRunSummary BuildSummary(string runId, string exitRuleCode, IReadOnlyList<BacktestSignalRow> signals, IReadOnlyList<BacktestTradeRow> trades)
        {
            int wins = trades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. trades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. trades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];

            return new BacktestRunSummary
            {
                RunId = runId,
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

        private static bool IsAfterBase(string dateTime, string baseDate)
        {
            string threshold = $"{BacktestDataStore.NormalizeDate(baseDate)}000000";
            return string.CompareOrdinal(dateTime, threshold) >= 0;
        }

        private sealed record BaseGroup(string Code, string Market, string BaseDate);
        private readonly record struct BaseState(
            string Time,
            long Open,
            long High,
            long Low,
            long Close,
            long TradingValue,
            decimal Ma60,
            decimal Center,
            bool IsSmallBase);
    }
}
