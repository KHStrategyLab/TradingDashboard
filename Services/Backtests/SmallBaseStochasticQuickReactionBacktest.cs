using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class SmallBaseStochasticQuickReactionBacktest
    {
        private const string StrategyCode = "STRONG_DAILY_STOCH_QUICK_REACTION";
        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public SmallBaseStochasticQuickReactionBacktest(
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
            int reactionBars = 2,
            decimal reactionR = 0.5m,
            decimal baseRisePercent = 1.0m,
            long baseTradingValueWon = 1_000_000_000,
            decimal maxChangeRate = 20.0m,
            long entryTradingValueWon = 300_000_000,
            decimal volumeMultiplier = 1.2m)
        {
            string exitRuleCode = $"QUICK_{reactionBars}B_{reactionR:0.##}R_REENTRY";
            string runId = _runStore.CreateRunId($"stoch_quick_reaction_{baseMinute}m_{entryMinute}m");
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
                List<BacktestDailyBar> dailyBars = [.. _dataStore.LoadDailyBars(group.Code, group.Market).OrderBy(bar => bar.Date)];
                List<BacktestBaseCandle> verifiedDailyBaseCandles = [.. _dataStore.LoadBaseCandles()
                    .Where(item =>
                        string.Equals(BacktestDataStore.NormalizeCode(item.Code), group.Code, StringComparison.Ordinal) &&
                        string.Equals(BacktestDataStore.NormalizeMarket(item.Market), group.Market, StringComparison.Ordinal))
                    .OrderBy(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate))];
                List<BacktestMinuteBar> baseBars = [.. _dataStore.LoadMinuteBars(group.Code, group.Market, baseMinute)
                    .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                    .OrderBy(bar => bar.DateTime)];
                List<BacktestMinuteBar> entryBars = [.. _dataStore.LoadMinuteBars(group.Code, group.Market, entryMinute)
                    .Where(bar => IsAfterBase(bar.DateTime, group.BaseDate))
                    .OrderBy(bar => bar.DateTime)];

                if (dailyBars.Count < 21 || baseBars.Count < 61 || entryBars.Count < 86)
                    continue;

                Dictionary<string, BaseState> baseStates = BuildBaseStateMap(baseBars, baseRisePercent, baseTradingValueWon);
                Dictionary<string, StochState> stochStates = BuildStochStateMap(entryBars);
                Dictionary<string, long> previousCloseByDate = dailyBars
                    .Where(bar => !string.IsNullOrWhiteSpace(bar.Date) && bar.PreviousClose > 0)
                    .GroupBy(bar => BacktestDataStore.NormalizeDate(bar.Date), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last().PreviousClose, StringComparer.Ordinal);

                int holdingBars = Math.Max(1, observationMinutes / Math.Max(1, entryMinute));
                for (int i = 61; i < entryBars.Count - Math.Max(holdingBars, reactionBars); i++)
                {
                    BacktestMinuteBar entryBar = entryBars[i];
                    if (!TryGetLatestBaseState(baseStates, entryBar.DateTime, out BaseState baseState) ||
                        !baseState.IsSmallBase)
                    {
                        continue;
                    }

                    string entryDate = entryBar.DateTime.Length >= 8 ? entryBar.DateTime[..8] : string.Empty;
                    BacktestBaseCandle? latestDailyBase = ResolveLatestVerifiedDailyBase(verifiedDailyBaseCandles, entryDate);
                    if (latestDailyBase == null || !IsStrongDailyBase(dailyBars, latestDailyBase))
                        continue;

                    if (!TryGetStochCross(stochStates, entryBars[i - 1].DateTime, entryBar.DateTime, out StochState stoch))
                        continue;

                    List<BacktestMinuteBar> previous20 = [.. entryBars.Skip(i - 20).Take(20)];
                    decimal avgVolume20 = previous20.Average(bar => (decimal)Math.Max(0, bar.Volume));
                    long previousClose = ResolvePreviousClose(previousCloseByDate, entryBar.DateTime);
                    if (!IsEntry(entryBar, previousClose, baseState, stoch, maxChangeRate, entryTradingValueWon, avgVolume20, volumeMultiplier))
                        continue;

                    AddQuickReactionTrade(
                        runId,
                        exitRuleCode,
                        group,
                        entryBars,
                        i,
                        holdingBars,
                        reactionBars,
                        reactionR,
                        observationMinutes,
                        baseState,
                        $"strong daily + stoch A; {baseMinute}m base {baseState.Time}; quick reaction {reactionBars} bars >= {reactionR:0.##}R",
                        signals,
                        trades);
                }
            }

            BacktestRunSummary summary = BuildSummary(runId, exitRuleCode, signals, trades);
            string outputDirectory = _runStore.SaveRun(runId, signals, trades, [summary]);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summary);
        }

        private static void AddQuickReactionTrade(
            string runId,
            string exitRuleCode,
            BaseGroup group,
            IReadOnlyList<BacktestMinuteBar> bars,
            int entryIndex,
            int holdingBars,
            int reactionBars,
            decimal reactionR,
            int observationMinutes,
            BaseState baseState,
            string reason,
            List<BacktestSignalRow> signals,
            List<BacktestTradeRow> trades)
        {
            BacktestMinuteBar entryBar = bars[entryIndex];
            long entryPrice = entryBar.Close;
            long stopPrice = baseState.Low;
            long riskWon = entryPrice - stopPrice;
            if (entryPrice <= 0 || riskWon <= 0)
                return;

            List<BacktestMinuteBar> fullWindow = ResolveHoldingBars(bars, entryIndex, holdingBars);
            List<BacktestMinuteBar> reactionWindow = ResolveHoldingBars(bars, entryIndex, reactionBars);
            if (fullWindow.Count < holdingBars || reactionWindow.Count < reactionBars)
                return;

            long reactionTarget = entryPrice + (long)Math.Ceiling(riskWon * reactionR);
            bool reacted = reactionWindow.Max(bar => bar.High) >= reactionTarget;
            IReadOnlyList<BacktestMinuteBar> resolvedWindow = reacted ? fullWindow : reactionWindow;
            BacktestMinuteBar exitBar = resolvedWindow.Last();
            long maxHigh = resolvedWindow.Max(bar => bar.High);
            long minLow = resolvedWindow.Min(bar => bar.Low);
            decimal mfe = (maxHigh - entryPrice) / (decimal)entryPrice * 100m;
            decimal mae = (minLow - entryPrice) / (decimal)entryPrice * 100m;
            decimal exitRate = (exitBar.Close - entryPrice) / (decimal)entryPrice * 100m;
            decimal riskRate = riskWon / (decimal)entryPrice * 100m;
            decimal maxR = (maxHigh - entryPrice) / (decimal)riskWon;
            decimal minR = (minLow - entryPrice) / (decimal)riskWon;
            int holdingMinutes = reacted ? observationMinutes : reactionBars * Math.Max(1, bars[entryIndex].Minute);

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
                HoldingMinutes = holdingMinutes,
                EntryReason = reason,
                ExitReason = reacted
                    ? $"{reactionBars}-bar reaction confirmed, then {observationMinutes}-minute quality window"
                    : $"{reactionBars}-bar no-reaction time exit"
            });
        }

        private static bool IsStrongDailyBase(IReadOnlyList<BacktestDailyBar> bars, BacktestBaseCandle baseCandle)
        {
            string baseDate = BacktestDataStore.NormalizeDate(baseCandle.BaseCandleDate);
            int index = -1;
            for (int i = 0; i < bars.Count; i++)
            {
                if (string.Equals(BacktestDataStore.NormalizeDate(bars[i].Date), baseDate, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 20)
                return false;

            BacktestDailyBar previous = bars[index - 1];
            decimal avg20 = bars.Skip(index - 20).Take(20).Average(bar => (decimal)bar.Close);
            double std20 = StdDev(bars.Skip(index - 20).Take(20).Select(bar => (double)bar.Close));
            decimal upper = avg20 + (decimal)(std20 * 2.0);
            return baseCandle.BaseTradingValue >= 100_000_000_000 &&
                baseCandle.BaseClose > upper &&
                baseCandle.BaseClose > previous.High &&
                baseCandle.BaseClose >= previous.High * 1.10m;
        }

        private static BacktestBaseCandle? ResolveLatestVerifiedDailyBase(IReadOnlyList<BacktestBaseCandle> verifiedDailyBaseCandles, string entryDate)
        {
            return verifiedDailyBaseCandles
                .Where(item => string.CompareOrdinal(BacktestDataStore.NormalizeDate(item.BaseCandleDate), entryDate) <= 0)
                .OrderBy(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate))
                .LastOrDefault();
        }

        private static bool IsEntry(
            BacktestMinuteBar entryBar,
            long previousClose,
            BaseState baseState,
            StochState stoch,
            decimal maxChangeRate,
            long entryTradingValueWon,
            decimal avgVolume20,
            decimal volumeMultiplier)
        {
            decimal changeRate = previousClose > 0 ? (entryBar.Close - previousClose) / (decimal)previousClose * 100m : 0m;
            bool centerSupport = entryBar.Close >= baseState.Center && entryBar.Close <= baseState.Close;
            bool kZone = stoch.K > 20m && stoch.K < 50m;
            bool bullish = entryBar.Close > entryBar.Open;
            bool notOverheated = changeRate < maxChangeRate;
            bool moneyOk = entryBar.TradingValue >= entryTradingValueWon;
            bool volumeOk = avgVolume20 <= 0 || entryBar.Volume >= avgVolume20 * volumeMultiplier;
            return centerSupport && kZone && bullish && notOverheated && moneyOk && volumeOk;
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
                    current.Low,
                    current.Close,
                    currentMa60,
                    (current.High + current.Low) / 2m,
                    isSmallBase);
            }

            return result;
        }

        private static Dictionary<string, StochState> BuildStochStateMap(IReadOnlyList<BacktestMinuteBar> bars)
        {
            var rawK = new decimal?[bars.Count];
            var slowK = new decimal?[bars.Count];
            var slowD = new decimal?[bars.Count];
            for (int i = 13; i < bars.Count; i++)
            {
                List<BacktestMinuteBar> window = [.. bars.Skip(i - 13).Take(14)];
                long high = window.Max(bar => bar.High);
                long low = window.Min(bar => bar.Low);
                rawK[i] = high > low ? (bars[i].Close - low) / (decimal)(high - low) * 100m : 50m;
            }

            for (int i = 15; i < bars.Count; i++)
            {
                decimal[] values = [.. rawK.Skip(i - 2).Take(3).Where(value => value.HasValue).Select(value => value!.Value)];
                if (values.Length == 3)
                    slowK[i] = values.Average();
            }

            for (int i = 17; i < bars.Count; i++)
            {
                decimal[] values = [.. slowK.Skip(i - 2).Take(3).Where(value => value.HasValue).Select(value => value!.Value)];
                if (values.Length == 3)
                    slowD[i] = values.Average();
            }

            var result = new Dictionary<string, StochState>(StringComparer.Ordinal);
            for (int i = 0; i < bars.Count; i++)
            {
                if (slowK[i].HasValue && slowD[i].HasValue)
                    result[bars[i].DateTime] = new StochState(slowK[i]!.Value, slowD[i]!.Value);
            }

            return result;
        }

        private static bool TryGetStochCross(
            IReadOnlyDictionary<string, StochState> states,
            string previousTime,
            string currentTime,
            out StochState current)
        {
            current = default;
            if (!states.TryGetValue(previousTime, out StochState previous) ||
                !states.TryGetValue(currentTime, out current))
            {
                return false;
            }

            return previous.K <= previous.D && current.K > current.D;
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

        private static long ResolvePreviousClose(IReadOnlyDictionary<string, long> previousCloseByDate, string dateTime)
        {
            string date = dateTime.Length >= 8 ? dateTime[..8] : string.Empty;
            return previousCloseByDate.TryGetValue(date, out long previousClose) ? previousClose : 0;
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

        private static double StdDev(IEnumerable<double> values)
        {
            double[] array = [.. values];
            if (array.Length == 0)
                return 0.0;

            double avg = array.Average();
            return Math.Sqrt(array.Sum(value => Math.Pow(value - avg, 2)) / array.Length);
        }

        private static bool IsAfterBase(string dateTime, string baseDate)
        {
            string threshold = $"{BacktestDataStore.NormalizeDate(baseDate)}000000";
            return string.CompareOrdinal(dateTime, threshold) >= 0;
        }

        private sealed record BaseGroup(string Code, string Market, string BaseDate);
        private readonly record struct BaseState(string Time, long Low, long Close, decimal Ma60, decimal Center, bool IsSmallBase);
        private readonly record struct StochState(decimal K, decimal D);
    }
}
