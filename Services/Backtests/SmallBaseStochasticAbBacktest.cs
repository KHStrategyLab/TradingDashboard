using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class SmallBaseStochasticAbBacktest
    {
        private const string BaseStrategyCode = "SMALL_BASE_STOCH_PULLBACK";
        private const string VariantA = "A_STOCH";
        private const string VariantB = "B_STOCH_DRY_REIGNITE";
        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public SmallBaseStochasticAbBacktest(
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
            decimal maxChangeRate = 20.0m,
            long entryTradingValueWon = 300_000_000,
            decimal volumeMultiplier = 1.2m,
            decimal dryTradingValueRatio = 0.7m,
            decimal smallBodyMinPercent = 0.8m,
            decimal smallBodyMaxPercent = 1.5m,
            decimal reigniteVolumeMultiplier = 2.0m)
        {
            string exitRuleCode = $"OBSERVE_{observationMinutes}M_R";
            string runId = _runStore.CreateRunId($"small_base_stoch_ab_{baseMinute}m_{entryMinute}m_hold{observationMinutes}");
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
                Dictionary<string, long> previousCloseByDate = LoadDailyBarsWithKrxFallback(group.Code, group.Market)
                    .Where(bar => !string.IsNullOrWhiteSpace(bar.Date) && bar.PreviousClose > 0)
                    .GroupBy(bar => BacktestDataStore.NormalizeDate(bar.Date), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last().PreviousClose, StringComparer.Ordinal);

                if (baseBars.Count < 61 || entryBars.Count < 86)
                    continue;

                Dictionary<string, BaseState> baseStates = BuildBaseStateMap(baseBars, baseRisePercent, baseTradingValueWon);
                Dictionary<string, StochState> stochStates = BuildStochStateMap(entryBars);
                var usedKeys = new HashSet<string>(StringComparer.Ordinal);
                int holdingBars = Math.Max(1, observationMinutes / Math.Max(1, entryMinute));

                for (int i = 61; i < entryBars.Count - holdingBars; i++)
                {
                    BacktestMinuteBar entryBar = entryBars[i];
                    if (!TryGetLatestBaseState(baseStates, entryBar.DateTime, out BaseState baseState) ||
                        !baseState.IsSmallBase)
                    {
                        continue;
                    }

                    List<BacktestMinuteBar> previous20 = [.. entryBars.Skip(i - 20).Take(20)];
                    decimal avgVolume20 = previous20.Average(bar => (decimal)Math.Max(0, bar.Volume));
                    decimal avgPreviousTradingValue10 = entryBars.Skip(i - 10).Take(10).Average(bar => (decimal)Math.Max(0, bar.TradingValue));
                    long previousClose = ResolvePreviousClose(previousCloseByDate, entryBar.DateTime);
                    bool hasStochCross = TryGetStochCross(stochStates, entryBars[i - 1].DateTime, entryBar.DateTime, out StochState stoch);
                    bool baseFilter = hasStochCross && IsVariantAEntry(
                        entryBar,
                        previousClose,
                        baseState,
                        stoch,
                        maxChangeRate,
                        entryTradingValueWon,
                        avgVolume20,
                        volumeMultiplier);

                    if (baseFilter)
                    {
                        AddTrade(
                            runId,
                            $"{BaseStrategyCode}_{VariantA}",
                            exitRuleCode,
                            group,
                            entryBars,
                            i,
                            holdingBars,
                            observationMinutes,
                            baseState.Low,
                            baseState,
                            $"A: {baseMinute}m small-base {baseState.Time}; {entryMinute}m center support + stoch GC K:{stoch.K:0.##}/D:{stoch.D:0.##}; change<{maxChangeRate:0.##}%; money>={entryTradingValueWon}; vol>{volumeMultiplier:0.##}x avg20",
                            usedKeys,
                            signals,
                            trades);
                    }

                    bool dryReigniteSetup = IsDryReignite(
                        entryBars[i - 1],
                        entryBar,
                        avgPreviousTradingValue10,
                        dryTradingValueRatio,
                        smallBodyMinPercent,
                        smallBodyMaxPercent,
                        entryTradingValueWon,
                        reigniteVolumeMultiplier);

                    if (dryReigniteSetup &&
                        IsBaseCenterArea(entryBar, previousClose, baseState, maxChangeRate) &&
                        i + 1 < entryBars.Count - holdingBars)
                    {
                        BacktestMinuteBar confirmationBar = entryBars[i + 1];
                        decimal setupCenter = (entryBar.High + entryBar.Low) / 2m;
                        if (IsReigniteConfirmation(entryBar, confirmationBar, setupCenter))
                        {
                            AddTrade(
                                runId,
                                $"{BaseStrategyCode}_{VariantB}",
                                exitRuleCode,
                                group,
                                entryBars,
                                i + 1,
                                holdingBars,
                                observationMinutes,
                                entryBar.Low,
                                baseState,
                                $"B: A + dry/reignite base registered {entryBar.DateTime}; next bar high break or setup-center rebound; setup H/C/L {entryBar.High}/{setupCenter:0.##}/{entryBar.Low}",
                                usedKeys,
                                signals,
                                trades);
                        }
                    }
                }
            }

            List<BacktestRunSummary> summaries =
            [
                BuildSummary(runId, $"{BaseStrategyCode}_{VariantA}", exitRuleCode, signals, trades),
                BuildSummary(runId, $"{BaseStrategyCode}_{VariantB}", exitRuleCode, signals, trades)
            ];

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries.First());
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

        private static bool IsVariantAEntry(
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

        private static bool IsBaseCenterArea(
            BacktestMinuteBar setupBar,
            long previousClose,
            BaseState baseState,
            decimal maxChangeRate)
        {
            decimal changeRate = previousClose > 0 ? (setupBar.Close - previousClose) / (decimal)previousClose * 100m : 0m;
            bool centerArea = setupBar.Close >= baseState.Center && setupBar.Close <= baseState.Close;
            bool notOverheated = changeRate < maxChangeRate;
            return centerArea && notOverheated;
        }

        private static bool IsDryReignite(
            BacktestMinuteBar previous,
            BacktestMinuteBar current,
            decimal avgPreviousTradingValue10,
            decimal dryTradingValueRatio,
            decimal smallBodyMinPercent,
            decimal smallBodyMaxPercent,
            long entryTradingValueWon,
            decimal reigniteVolumeMultiplier)
        {
            decimal body = current.Open > 0 ? (current.Close - current.Open) / (decimal)current.Open * 100m : 0m;
            bool dry = avgPreviousTradingValue10 <= 0 || previous.TradingValue <= avgPreviousTradingValue10 * dryTradingValueRatio;
            bool small = body >= smallBodyMinPercent && body <= smallBodyMaxPercent;
            bool money = current.TradingValue >= entryTradingValueWon;
            bool volumeUp = previous.Volume <= 0 || current.Volume >= previous.Volume * reigniteVolumeMultiplier;
            return dry && small && money && volumeUp;
        }

        private static bool IsReigniteConfirmation(
            BacktestMinuteBar setupBar,
            BacktestMinuteBar confirmationBar,
            decimal setupCenter)
        {
            bool highBreak = confirmationBar.Close > setupBar.High;
            bool centerRebound = confirmationBar.Low <= setupCenter &&
                confirmationBar.Close >= setupCenter &&
                confirmationBar.Close > confirmationBar.Open;
            return highBreak || centerRebound;
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

        private static void AddTrade(
            string runId,
            string strategyCode,
            string exitRuleCode,
            BaseGroup group,
            IReadOnlyList<BacktestMinuteBar> bars,
            int entryIndex,
            int holdingBars,
            int observationMinutes,
            long stopPrice,
            BaseState baseState,
            string reason,
            ISet<string> usedKeys,
            List<BacktestSignalRow> signals,
            List<BacktestTradeRow> trades)
        {
            BacktestMinuteBar entryBar = bars[entryIndex];
            string usedKey = $"{strategyCode}|{group.Code}|{group.Market}|{baseState.Time}|{entryBar.DateTime}";
            if (!usedKeys.Add(usedKey))
                return;

            List<BacktestMinuteBar> holdingWindow = ResolveHoldingBars(bars, entryIndex, holdingBars);
            if (holdingWindow.Count < holdingBars)
                return;

            long entryPrice = entryBar.Close;
            long riskWon = entryPrice - stopPrice;
            if (entryPrice <= 0 || riskWon <= 0)
                return;

            long maxHigh = holdingWindow.Max(bar => bar.High);
            long minLow = holdingWindow.Min(bar => bar.Low);
            BacktestMinuteBar exitBar = holdingWindow.Last();
            decimal mfe = (maxHigh - entryPrice) / (decimal)entryPrice * 100m;
            decimal mae = (minLow - entryPrice) / (decimal)entryPrice * 100m;
            decimal exitRate = (exitBar.Close - entryPrice) / (decimal)entryPrice * 100m;
            decimal riskRate = riskWon / (decimal)entryPrice * 100m;
            decimal maxR = (maxHigh - entryPrice) / (decimal)riskWon;
            decimal minR = (minLow - entryPrice) / (decimal)riskWon;

            signals.Add(new BacktestSignalRow
            {
                RunId = runId,
                StrategyCode = strategyCode,
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
                StrategyCode = strategyCode,
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
        }

        private static BacktestRunSummary BuildSummary(
            string runId,
            string strategyCode,
            string exitRuleCode,
            IReadOnlyList<BacktestSignalRow> signals,
            IReadOnlyList<BacktestTradeRow> trades)
        {
            List<BacktestTradeRow> filteredTrades = [.. trades.Where(trade => string.Equals(trade.StrategyCode, strategyCode, StringComparison.Ordinal))];
            int signalCount = signals.Count(signal => string.Equals(signal.StrategyCode, strategyCode, StringComparison.Ordinal));
            int wins = filteredTrades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. filteredTrades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. filteredTrades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];

            return new BacktestRunSummary
            {
                RunId = runId,
                StrategyCode = strategyCode,
                ExitRuleCode = exitRuleCode,
                SignalCount = signalCount,
                TradeCount = filteredTrades.Count,
                WinRate = filteredTrades.Count > 0 ? wins / (decimal)filteredTrades.Count * 100m : 0m,
                AvgProfit = profits.Count > 0 ? profits.Average() : 0m,
                AvgLoss = losses.Count > 0 ? losses.Average() : 0m,
                Expectancy = filteredTrades.Count > 0 ? filteredTrades.Average(trade => trade.ProfitRate) : 0m,
                TotalProfit = filteredTrades.Sum(trade => trade.ProfitAmount),
                MaxDrawdown = filteredTrades.Count > 0 ? filteredTrades.Min(trade => trade.Mae) : 0m,
                MAE = filteredTrades.Count > 0 ? filteredTrades.Average(trade => trade.Mae) : 0m,
                MFE = filteredTrades.Count > 0 ? filteredTrades.Average(trade => trade.Mfe) : 0m,
                AvgHoldingMinutes = filteredTrades.Count > 0 ? filteredTrades.Average(trade => (decimal)trade.HoldingMinutes) : 0m,
                ConsecutiveLosses = ResolveMaxConsecutiveLosses(filteredTrades),
                FeeAdjustedProfit = filteredTrades.Sum(trade => trade.ProfitAmount),
                SlippageAdjustedProfit = filteredTrades.Sum(trade => trade.ProfitAmount)
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

        private static bool IsAfterBase(string dateTime, string baseDate)
        {
            string threshold = $"{BacktestDataStore.NormalizeDate(baseDate)}000000";
            return string.CompareOrdinal(dateTime, threshold) >= 0;
        }

        private IReadOnlyList<BacktestDailyBar> LoadDailyBarsWithKrxFallback(string code, string market)
        {
            IReadOnlyList<BacktestDailyBar> bars = _dataStore.LoadDailyBars(code, market);
            if (bars.Count > 0 || string.Equals(BacktestDataStore.NormalizeMarket(market), "KRX", StringComparison.Ordinal))
                return bars;

            return _dataStore.LoadDailyBars(code, "KRX");
        }

        private sealed record BaseGroup(string Code, string Market, string BaseDate);
        private readonly record struct BaseState(string Time, long Low, long Close, decimal Ma60, decimal Center, bool IsSmallBase);
        private readonly record struct StochState(decimal K, decimal D);
    }
}
