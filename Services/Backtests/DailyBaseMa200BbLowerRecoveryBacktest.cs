using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class DailyBaseMa200BbLowerRecoveryBacktest
    {
        public const string StrategyCode = "DAILY_BASE_MA200_BBLOWER_RECOVERY_5M";
        public const string ExitRuleCode = "OBSERVE_180M_CLOSE";

        private const int Minute = 5;
        private const int ObservationMinutes = 180;
        private const int BollingerPeriod = 20;
        private const int MaPeriod = 200;
        private const int TradingValueAveragePeriod = 20;
        private const int TrackingTradingDays = 6;
        private const long MinimumTradingValueWon = 3_000_000_000;
        private const decimal TradingValueIncreaseMultiplier = 1.2m;

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public DailyBaseMa200BbLowerRecoveryBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run()
        {
            string runId = _runStore.CreateRunId("daily_base_ma200_bblower_recovery_5m_observe180");
            var signals = new List<BacktestSignalRow>();
            var trades = new List<BacktestTradeRow>();
            int holdingBars = Math.Max(1, ObservationMinutes / Minute);

            foreach (StockMarketKey stock in EnumerateFiveMinuteStockMarkets())
            {
                List<BacktestBaseCandle> baseCandles = [.. _dataStore.LoadBaseCandles()
                    .Where(item =>
                        string.Equals(BacktestDataStore.NormalizeCode(item.Code), stock.Code, StringComparison.Ordinal) &&
                        string.Equals(BacktestDataStore.NormalizeMarket(item.Market), stock.Market, StringComparison.Ordinal))
                    .OrderBy(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate))];
                if (baseCandles.Count == 0)
                    continue;

                List<BacktestDailyBar> dailyBars = [.. LoadDailyBarsForTracking(stock)
                    .OrderBy(bar => BacktestDataStore.NormalizeDate(bar.Date))];
                List<BacktestMinuteBar> bars = [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, Minute)
                    .OrderBy(item => item.DateTime)];
                if (dailyBars.Count < 2 || bars.Count < MaPeriod + holdingBars + 1)
                    continue;

                Dictionary<string, int> dailyIndexByDate = BuildDailyIndexByDate(dailyBars);
                Dictionary<int, IndicatorState> indicators = BuildIndicatorStates(bars);
                var usedDateSignals = new HashSet<string>(StringComparer.Ordinal);

                for (int i = Math.Max(MaPeriod, TradingValueAveragePeriod); i < bars.Count - holdingBars; i++)
                {
                    BacktestMinuteBar previous = bars[i - 1];
                    BacktestMinuteBar current = bars[i];
                    string signalDate = ResolveDate(current.DateTime);
                    if (string.IsNullOrWhiteSpace(signalDate))
                        continue;

                    if (!TryResolveTrackingBase(baseCandles, dailyIndexByDate, signalDate, out BacktestBaseCandle baseCandle, out int offset))
                        continue;

                    if (!indicators.TryGetValue(i - 1, out IndicatorState previousState) ||
                        !indicators.TryGetValue(i, out IndicatorState currentState))
                    {
                        continue;
                    }

                    decimal averageTradingValue = bars
                        .Skip(i - TradingValueAveragePeriod)
                        .Take(TradingValueAveragePeriod)
                        .Average(item => (decimal)Math.Max(0, item.TradingValue));

                    if (!TryResolveSignalKind(previous, current, previousState, currentState, averageTradingValue, out string signalKind))
                        continue;

                    string dateSignalKey = $"{stock.Code}|{stock.Market}|{signalDate}|{signalKind}";
                    if (!usedDateSignals.Add(dateSignalKey))
                        continue;

                    List<BacktestMinuteBar> holding = ResolveHoldingBars(bars, i, holdingBars);
                    if (holding.Count == 0)
                        continue;

                    long entryPrice = current.Close;
                    long maxHigh = holding.Max(item => item.High);
                    long minLow = holding.Min(item => item.Low);
                    BacktestMinuteBar exitBar = holding[^1];
                    decimal mfe = entryPrice > 0 ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    decimal mae = entryPrice > 0 ? (minLow - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    decimal profitRate = entryPrice > 0 ? (exitBar.Close - entryPrice) / (decimal)entryPrice * 100m : 0m;

                    string reason = BuildEntryReason(signalKind, current, currentState, averageTradingValue, baseCandle, offset);
                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = stock.Code,
                        Market = stock.Market,
                        SignalTime = current.DateTime,
                        SignalType = "BUY_AUX",
                        Price = entryPrice,
                        Reason = reason
                    });

                    trades.Add(new BacktestTradeRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        ExitRuleCode = ExitRuleCode,
                        Code = stock.Code,
                        Market = stock.Market,
                        EntryTime = current.DateTime,
                        ExitTime = exitBar.DateTime,
                        EntryPrice = entryPrice,
                        ExitPrice = exitBar.Close,
                        MaxHigh = maxHigh,
                        MinLow = minLow,
                        StopPrice = 0,
                        Quantity = 1,
                        ProfitRate = profitRate,
                        ProfitAmount = exitBar.Close - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        RiskRate = 0,
                        MaxR = 0,
                        MinR = 0,
                        HoldingMinutes = ResolveHoldingMinutes(current.DateTime, exitBar.DateTime, holding.Count * Minute),
                        EntryReason = reason,
                        ExitReason = $"observe {ObservationMinutes}m close only; maxHigh MFE {mfe:0.##}%, minLow MAE {mae:0.##}%"
                    });
                }
            }

            List<BacktestRunSummary> summaries =
            [
                BuildSummary(runId, "MIXED", signals, trades),
                BuildSummary(runId, "KRX", signals.Where(item => item.Market == "KRX").ToList(), trades.Where(item => item.Market == "KRX").ToList()),
                BuildSummary(runId, "NXT", signals.Where(item => item.Market == "NXT").ToList(), trades.Where(item => item.Market == "NXT").ToList()),
                BuildSummary(runId, "AL", signals.Where(item => item.Market == "AL").ToList(), trades.Where(item => item.Market == "AL").ToList())
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
                Memo = "Auxiliary daily-base context test. After a verified daily base candle, track 5m BBandsDown(20,2) / MA200 recovery signals for up to 6 trading days. Requires 5m trading value >=30eok and >=20-bar average * 1.2. No live orders."
            };

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries, config);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries[0]);
        }

        private IEnumerable<StockMarketKey> EnumerateFiveMinuteStockMarkets()
        {
            string directory = Path.Combine(_dataStore.RootPath, "minute", "5m");
            if (!Directory.Exists(directory))
                yield break;

            var stocks = new List<StockMarketKey>();
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

                stocks.Add(new StockMarketKey(code, market));
            }

            foreach (IGrouping<string, StockMarketKey> group in stocks.GroupBy(stock => stock.Code, StringComparer.Ordinal))
            {
                StockMarketKey? al = group.FirstOrDefault(stock => string.Equals(stock.Market, "AL", StringComparison.Ordinal));
                if (al != null)
                {
                    yield return al;
                    continue;
                }

                foreach (StockMarketKey stock in group.OrderBy(stock => stock.Market, StringComparer.Ordinal))
                    yield return stock;
            }
        }

        private IReadOnlyList<BacktestDailyBar> LoadDailyBarsForTracking(StockMarketKey stock)
        {
            if (!string.Equals(stock.Market, "AL", StringComparison.Ordinal))
                return _dataStore.LoadDailyBars(stock.Code, stock.Market);

            IReadOnlyList<BacktestDailyBar> alBars = _dataStore.LoadDailyBars(stock.Code, "AL");
            return alBars.Count > 0
                ? alBars
                : _dataStore.LoadDailyBars(stock.Code, "KRX");
        }

        private static Dictionary<string, int> BuildDailyIndexByDate(IReadOnlyList<BacktestDailyBar> dailyBars) =>
            dailyBars
                .Select((bar, index) => new { Date = BacktestDataStore.NormalizeDate(bar.Date), Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.Date))
                .GroupBy(item => item.Date, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);

        private static bool TryResolveTrackingBase(
            IReadOnlyList<BacktestBaseCandle> baseCandles,
            IReadOnlyDictionary<string, int> dailyIndexByDate,
            string signalDate,
            out BacktestBaseCandle baseCandle,
            out int offset)
        {
            baseCandle = default!;
            offset = -1;
            if (!dailyIndexByDate.TryGetValue(signalDate, out int signalIndex))
                return false;

            foreach (BacktestBaseCandle candidate in baseCandles.OrderByDescending(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate)))
            {
                string baseDate = BacktestDataStore.NormalizeDate(candidate.BaseCandleDate);
                if (!dailyIndexByDate.TryGetValue(baseDate, out int baseIndex))
                    continue;

                int resolvedOffset = signalIndex - baseIndex;
                if (resolvedOffset < 0 || resolvedOffset > TrackingTradingDays)
                    continue;

                baseCandle = candidate;
                offset = resolvedOffset;
                return true;
            }

            return false;
        }

        private static Dictionary<int, IndicatorState> BuildIndicatorStates(IReadOnlyList<BacktestMinuteBar> bars)
        {
            var result = new Dictionary<int, IndicatorState>();
            for (int i = MaPeriod - 1; i < bars.Count; i++)
            {
                decimal ma200 = bars.Skip(i - MaPeriod + 1).Take(MaPeriod).Average(item => (decimal)item.Close);
                if (i < BollingerPeriod - 1)
                    continue;

                List<decimal> closes = [.. bars.Skip(i - BollingerPeriod + 1).Take(BollingerPeriod).Select(item => (decimal)item.Close)];
                decimal average = closes.Average();
                double variance = closes.Select(item => Math.Pow((double)(item - average), 2)).Average();
                decimal standardDeviation = (decimal)Math.Sqrt(variance);
                decimal lower = average - standardDeviation * 2m;
                result[i] = new IndicatorState(lower, ma200);
            }

            return result;
        }

        private static bool TryResolveSignalKind(
            BacktestMinuteBar previous,
            BacktestMinuteBar current,
            IndicatorState previousState,
            IndicatorState currentState,
            decimal averageTradingValue,
            out string signalKind)
        {
            signalKind = string.Empty;
            bool ma200Rising = currentState.Ma200 > previousState.Ma200;
            bool tradingValueOk = current.TradingValue >= MinimumTradingValueWon;
            bool tradingValueIncreasing = averageTradingValue <= 0 ||
                current.TradingValue >= averageTradingValue * TradingValueIncreaseMultiplier;
            if (!ma200Rising || !tradingValueOk || !tradingValueIncreasing)
                return false;

            bool buyPoint1 =
                currentState.BollingerLower > currentState.Ma200 &&
                current.Low < currentState.BollingerLower &&
                previous.Close <= previousState.BollingerLower &&
                current.Close > currentState.BollingerLower;

            if (buyPoint1)
            {
                signalKind = "BUYPOINT1_BBLOWER_ABOVE_MA200_REBOUND";
                return true;
            }

            bool buyPoint2 =
                currentState.BollingerLower < currentState.Ma200 &&
                current.Low <= currentState.BollingerLower * 1.01m &&
                current.Close >= currentState.BollingerLower &&
                previous.Close <= previousState.Ma200 &&
                current.Close > currentState.Ma200;

            if (buyPoint2)
            {
                signalKind = "BUYPOINT2_BBLOWER_SUPPORT_MA200_RECOVER";
                return true;
            }

            return false;
        }

        private static string BuildEntryReason(
            string signalKind,
            BacktestMinuteBar bar,
            IndicatorState state,
            decimal averageTradingValue,
            BacktestBaseCandle baseCandle,
            int offset) =>
            $"{signalKind}; dailyBase {BacktestDataStore.NormalizeDate(baseCandle.BaseCandleDate)} D+{offset}; baseValue {baseCandle.BaseTradingValue / 100_000_000m:0.##}eok; baseChange {baseCandle.ChangeRate:0.##}%; 5m value {bar.TradingValue / 100_000_000m:0.##}eok; avg20value {averageTradingValue / 100_000_000m:0.##}eok; close {bar.Close:N0}; low {bar.Low:N0}; BBLower {state.BollingerLower:N0}; MA200 {state.Ma200:N0}";

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
                ExitRuleCode = ExitRuleCode,
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
        private readonly record struct IndicatorState(decimal BollingerLower, decimal Ma200);
    }
}
