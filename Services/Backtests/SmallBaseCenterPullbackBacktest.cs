using System;
using System.Collections.Generic;
using System.Globalization;
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
            int triggerMinute = 0,
            int observationMinutes = 180,
            decimal baseRisePercent = 1.0m,
            long baseTradingValueWon = 1_000_000_000,
            decimal entryVolumeMultiplier = 1.2m,
            decimal triggerVolumeMultiplier = 1.2m,
            bool useSignalExit = false)
        {
            int resolvedTriggerMinute = triggerMinute > 0 ? triggerMinute : entryMinute;
            int holdingBars = Math.Max(1, observationMinutes / Math.Max(1, resolvedTriggerMinute));
            string exitRuleCode = useSignalExit ? "SIGNAL_EXIT_1M_MA5_BASE_LOW_15M_STRUCTURE_MAX180" : $"OBSERVE_{observationMinutes}M_R";
            string runId = _runStore.CreateRunId(useSignalExit
                ? $"small_base_center_{baseMinute}m_{entryMinute}m_{resolvedTriggerMinute}m_signal_exit{observationMinutes}"
                : $"small_base_center_{baseMinute}m_{entryMinute}m_{resolvedTriggerMinute}m_hold{observationMinutes}");
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
                List<BacktestMinuteBar> triggerBars = resolvedTriggerMinute == entryMinute
                    ? entryBars
                    : [.. _dataStore.LoadMinuteBars(group.Code, group.Market, resolvedTriggerMinute)
                        .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                        .OrderBy(bar => bar.DateTime)];
                List<BacktestMinuteBar> oneBars = useSignalExit && resolvedTriggerMinute == 1
                    ? triggerBars
                    : [];
                List<BacktestMinuteBar> fifteenBars = useSignalExit
                    ? [.. _dataStore.LoadMinuteBars(group.Code, group.Market, 15)
                        .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                        .OrderBy(bar => bar.DateTime)]
                    : [];

                if (baseBars.Count < 61 || entryBars.Count < 22 || triggerBars.Count < 22 + holdingBars)
                    continue;
                if (useSignalExit && (oneBars.Count < 25 || fifteenBars.Count < 6))
                    continue;

                Dictionary<string, BaseState> baseStates = BuildBaseStateMap(baseBars, baseRisePercent, baseTradingValueWon);
                Dictionary<string, MinuteState> oneStateByTime = useSignalExit ? BuildMinuteStateMap(oneBars, 5) : [];
                Dictionary<string, MinuteState> baseMinuteStateByTime = useSignalExit ? BuildMinuteStateMap(baseBars, 5) : [];
                Dictionary<string, MinuteState> fifteenStateByTime = useSignalExit ? BuildMinuteStateMap(fifteenBars, 5) : [];
                var usedBaseTimes = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 21; i < triggerBars.Count - holdingBars; i++)
                {
                    BacktestMinuteBar triggerBar = triggerBars[i];
                    if (!TryGetLatestBaseState(baseStates, triggerBar.DateTime, out BaseState baseState) ||
                        !baseState.IsSmallBase ||
                        usedBaseTimes.Contains(baseState.Time))
                    {
                        continue;
                    }

                    if (!TryGetLatestEntrySupport(entryBars, triggerBar.DateTime, baseState, entryVolumeMultiplier, out BacktestMinuteBar supportBar))
                        continue;

                    BacktestMinuteBar previousTrigger = triggerBars[i - 1];
                    List<BacktestMinuteBar> previousTrigger20 = triggerBars.GetRange(i - 20, 20);
                    decimal avgTriggerVolume20 = previousTrigger20.Average(bar => (decimal)Math.Max(0, bar.Volume));
                    if (!IsTriggerEntry(triggerBar, previousTrigger, avgTriggerVolume20, triggerVolumeMultiplier))
                        continue;

                    long entryPrice = triggerBar.Close;
                    long stopPrice = baseState.Low;
                    long riskWon = entryPrice - stopPrice;
                    if (entryPrice <= 0 || riskWon <= 0)
                        continue;

                    if (!TryResolveExit(
                        group,
                        triggerBar,
                        triggerBars,
                        i,
                        holdingBars,
                        useSignalExit,
                        baseState,
                        oneBars,
                        oneStateByTime,
                        baseMinuteStateByTime,
                        fifteenStateByTime,
                        observationMinutes,
                        out ExitResult exit))
                    {
                        continue;
                    }

                    long maxHigh = exit.MaxHigh;
                    long minLow = exit.MinLow;
                    decimal mfe = (maxHigh - entryPrice) / (decimal)entryPrice * 100m;
                    decimal mae = (minLow - entryPrice) / (decimal)entryPrice * 100m;
                    decimal exitRate = (exit.ExitPrice - entryPrice) / (decimal)entryPrice * 100m;
                    decimal riskRate = riskWon / (decimal)entryPrice * 100m;
                    decimal maxR = (maxHigh - entryPrice) / (decimal)riskWon;
                    decimal minR = (minLow - entryPrice) / (decimal)riskWon;

                    string reason = $"{baseMinute}m small-base MA60 recover {baseState.Time}; {entryMinute}m center support {supportBar.DateTime}; {resolvedTriggerMinute}m trigger bullish above prev high; vol>{triggerVolumeMultiplier:0.##}x avg20";
                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = group.Code,
                        Market = group.Market,
                        SignalTime = triggerBar.DateTime,
                        SignalType = "BUY",
                        Price = entryPrice,
                        Reason = reason
                    });

                    if (useSignalExit)
                    {
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
                    }

                    trades.Add(new BacktestTradeRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        ExitRuleCode = exitRuleCode,
                        Code = group.Code,
                        Market = group.Market,
                        EntryTime = triggerBar.DateTime,
                        ExitTime = exit.ExitTime,
                        EntryPrice = entryPrice,
                        ExitPrice = exit.ExitPrice,
                        MaxHigh = maxHigh,
                        MinLow = minLow,
                        StopPrice = stopPrice,
                        Quantity = 1,
                        ProfitRate = exitRate,
                        ProfitAmount = exit.ExitPrice - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        RiskRate = riskRate,
                        MaxR = maxR,
                        MinR = minR,
                        HoldingMinutes = exit.HoldingMinutes,
                        EntryReason = reason,
                        ExitReason = exit.ExitReason
                    });

                    usedBaseTimes.Add(baseState.Time);
                }
            }

            BacktestRunSummary summary = BuildSummary(runId, exitRuleCode, signals, trades);
            string outputDirectory = _runStore.SaveRun(runId, signals, trades, [summary]);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summary);
        }

        private static bool TryResolveExit(
            BaseGroup group,
            BacktestMinuteBar entryBar,
            IReadOnlyList<BacktestMinuteBar> triggerBars,
            int signalIndex,
            int holdingBars,
            bool useSignalExit,
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyDictionary<string, MinuteState> oneStateByTime,
            IReadOnlyDictionary<string, MinuteState> baseMinuteStateByTime,
            IReadOnlyDictionary<string, MinuteState> fifteenStateByTime,
            int maxHoldingMinutes,
            out ExitResult result)
        {
            result = default;

            if (!useSignalExit)
            {
                List<BacktestMinuteBar> holdingWindow = ResolveHoldingBars(triggerBars, signalIndex, holdingBars);
                if (holdingWindow.Count < holdingBars)
                    return false;

                BacktestMinuteBar exitBar = holdingWindow.Last();
                result = new ExitResult(
                    exitBar.DateTime,
                    exitBar.Close,
                    holdingWindow.Max(bar => bar.High),
                    holdingWindow.Min(bar => bar.Low),
                    maxHoldingMinutes,
                    $"{maxHoldingMinutes}-minute signal-quality window");
                return true;
            }

            int entryIndex = FindTimeIndex(oneBars, entryBar.DateTime);
            if (entryIndex < 0 || entryIndex + 1 >= oneBars.Count)
                return false;

            string entryDate = entryBar.DateTime.Length >= 8 ? entryBar.DateTime[..8] : string.Empty;
            long entryPrice = entryBar.Close;
            long maxHigh = entryBar.High;
            long minLow = entryBar.Low;

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
                bool baseMinuteWeak = TryGetLatestMinuteState(baseMinuteStateByTime, current.DateTime, out MinuteState baseFrameState) &&
                    baseFrameState.Close < baseFrameState.Ma5;
                bool fifteenWeak = TryGetLatestMinuteState(fifteenStateByTime, current.DateTime, out MinuteState fifteenState) &&
                    fifteenState.Close < fifteenState.Ma5;
                bool baseLowBroken = baseState.Low > 0 && current.Close <= baseState.Low;

                string? exitReason = ResolveSignalExitReason(
                    group.Market,
                    current,
                    profitRate,
                    oneWeak,
                    baseMinuteWeak,
                    fifteenWeak,
                    baseLowBroken,
                    maxHoldingMinutes,
                    holdingMinutes);

                if (string.IsNullOrWhiteSpace(exitReason))
                    continue;

                result = new ExitResult(
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

        private static bool TryGetLatestEntrySupport(
            IReadOnlyList<BacktestMinuteBar> entryBars,
            string triggerTime,
            BaseState baseState,
            decimal entryVolumeMultiplier,
            out BacktestMinuteBar supportBar)
        {
            supportBar = default!;
            int index = -1;
            for (int i = entryBars.Count - 1; i >= 0; i--)
            {
                if (string.CompareOrdinal(entryBars[i].DateTime, triggerTime) < 0)
                {
                    index = i;
                    break;
                }
            }

            if (index < 20)
                return false;

            BacktestMinuteBar candidate = entryBars[index];
            List<BacktestMinuteBar> previous20 = [.. entryBars.Skip(index - 20).Take(20)];
            decimal avgVolume20 = previous20.Average(bar => (decimal)Math.Max(0, bar.Volume));
            bool centerSupport = candidate.Close >= baseState.Center && candidate.Close <= baseState.Close;
            bool volumeOk = avgVolume20 <= 0 || candidate.Volume >= avgVolume20 * entryVolumeMultiplier;
            if (!centerSupport || !volumeOk)
                return false;

            supportBar = candidate;
            return true;
        }

        private static bool IsTriggerEntry(
            BacktestMinuteBar triggerBar,
            BacktestMinuteBar previousTrigger,
            decimal avgVolume20,
            decimal triggerVolumeMultiplier)
        {
            bool bullishTurn = triggerBar.Close > triggerBar.Open;
            bool highBreak = triggerBar.Close > previousTrigger.High;
            bool volumeOk = avgVolume20 <= 0 || triggerBar.Volume >= avgVolume20 * triggerVolumeMultiplier;
            return bullishTurn && highBreak && volumeOk;
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

        private static string? ResolveSignalExitReason(
            string market,
            BacktestMinuteBar current,
            decimal profitRate,
            bool oneWeak,
            bool baseMinuteWeak,
            bool fifteenWeak,
            bool baseLowBroken,
            int maxHoldingMinutes,
            int holdingMinutes)
        {
            if (baseLowBroken)
                return $"5m base low broken at {current.Close:N0}";

            if (fifteenWeak)
                return "15m MA5 flow damaged";

            if (baseMinuteWeak)
                return $"base-frame MA5 structural weakness, pnl {profitRate:0.##}%";

            if (oneWeak)
                return $"1m MA5 down-cross, pnl {profitRate:0.##}%";

            if (holdingMinutes >= maxHoldingMinutes)
                return $"max holding {maxHoldingMinutes}m reached ({market})";

            return null;
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
        private readonly record struct ExitResult(
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            int HoldingMinutes,
            string ExitReason);
        private readonly record struct MinuteState(
            string Time,
            long Close,
            decimal Ma5,
            long PreviousClose,
            decimal PreviousMa5);
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
