using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class PrevHighPullbackOneBarRsiBacktest
    {
        public const string StrategyCode = "PREDAY_HIGH_PULLBACK_ONE_BAR_RSI2_BREAKOUT";
        public const string ExitRuleCode = "OBSERVE_180M_CLOSE";

        private const int Minute = 5;
        private const int ObservationMinutes = 180;
        private const int Ma5Period = 5;
        private const int Ma10Period = 10;
        private const int RsiPeriod = 2;

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public PrevHighPullbackOneBarRsiBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run()
        {
            string runId = _runStore.CreateRunId("preday_high_pullback_one_bar_rsi2_5m_observe180");
            var signals = new List<BacktestSignalRow>();
            var trades = new List<BacktestTradeRow>();
            int holdingBars = Math.Max(1, ObservationMinutes / Minute);

            foreach (StockMarketKey stock in EnumerateFiveMinuteStockMarkets())
            {
                List<BacktestMinuteBar> fiveBars = [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, Minute)
                    .OrderBy(item => item.DateTime)];
                List<BacktestDailyBar> dailyBars = [.. _dataStore.LoadDailyBars(stock.Code, stock.Market)
                    .OrderBy(item => BacktestDataStore.NormalizeDate(item.Date))];

                if (fiveBars.Count < Ma10Period + holdingBars + 2 || dailyBars.Count < 2)
                    continue;

                Dictionary<string, PreviousDayState> previousDayByDate = BuildPreviousDayMap(dailyBars);
                Dictionary<int, MaState> maByIndex = BuildMaStates(fiveBars);
                Dictionary<int, decimal> rsiByIndex = BuildRsiStates(fiveBars, RsiPeriod);
                var usedDate = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 1; i < fiveBars.Count - holdingBars; i++)
                {
                    BacktestMinuteBar previous = fiveBars[i - 1];
                    BacktestMinuteBar current = fiveBars[i];
                    string date = ResolveDate(current.DateTime);
                    if (string.IsNullOrWhiteSpace(date) || usedDate.Contains(date))
                        continue;

                    if (!string.Equals(date, ResolveDate(previous.DateTime), StringComparison.Ordinal))
                        continue;

                    if (!previousDayByDate.TryGetValue(date, out PreviousDayState previousDay) ||
                        previousDay.High <= 0 ||
                        !maByIndex.TryGetValue(i - 1, out MaState previousMa) ||
                        !rsiByIndex.TryGetValue(i, out decimal rsi2))
                    {
                        continue;
                    }

                    long p = previousDay.High;
                    bool previousAboveP = previous.Close > p;
                    bool previousMa5Support = previous.Low <= previousMa.Ma5 && previous.Close >= previousMa.Ma5;
                    bool previousMa10Support = previous.Low <= previousMa.Ma10 && previous.Close >= previousMa.Ma10;
                    bool currentCrossesPreviousHigh = previous.Close <= previous.High && current.Close > previous.High;
                    bool currentAboveP = current.Close > p;
                    bool currentBullish = current.Close > current.Open;
                    bool rsiOk = rsi2 > 50m;

                    if (!previousAboveP ||
                        !(previousMa5Support || previousMa10Support) ||
                        !currentCrossesPreviousHigh ||
                        !currentAboveP ||
                        !currentBullish ||
                        !rsiOk)
                    {
                        continue;
                    }

                    List<BacktestMinuteBar> holding = ResolveHoldingBars(fiveBars, i, holdingBars);
                    if (holding.Count == 0)
                        continue;

                    long entryPrice = current.Close;
                    long maxHigh = holding.Max(item => item.High);
                    long minLow = holding.Min(item => item.Low);
                    BacktestMinuteBar exitBar = holding[^1];
                    decimal mfe = entryPrice > 0 ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    decimal mae = entryPrice > 0 ? (minLow - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    decimal profitRate = entryPrice > 0 ? (exitBar.Close - entryPrice) / (decimal)entryPrice * 100m : 0m;
                    string reason =
                        $"BUY; PrevHighPullbackOneBarRsi2; previousHigh {p:N0}; prevPullbackHigh {previous.High:N0}; prevPullbackLow {previous.Low:N0}; MA5 {previousMa.Ma5:N0}; MA10 {previousMa.Ma10:N0}; RSI2 {rsi2:0.##}; entry {entryPrice:N0}; value {current.TradingValue / 100_000_000m:0.##}eok";

                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = StrategyCode,
                        Code = stock.Code,
                        Market = stock.Market,
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
                        Code = stock.Code,
                        Market = stock.Market,
                        EntryTime = current.DateTime,
                        ExitTime = exitBar.DateTime,
                        EntryPrice = entryPrice,
                        ExitPrice = exitBar.Close,
                        MaxHigh = maxHigh,
                        MinLow = minLow,
                        StopPrice = previous.Low,
                        Quantity = 1,
                        ProfitRate = profitRate,
                        ProfitAmount = exitBar.Close - entryPrice,
                        Mae = mae,
                        Mfe = mfe,
                        RiskRate = entryPrice > 0 ? (entryPrice - previous.Low) / (decimal)entryPrice * 100m : 0m,
                        MaxR = 0,
                        MinR = 0,
                        HoldingMinutes = ResolveHoldingMinutes(current.DateTime, exitBar.DateTime, holding.Count * Minute),
                        EntryReason = reason,
                        ExitReason = $"observe {ObservationMinutes}m close only; maxHigh MFE {mfe:0.##}%, minLow MAE {mae:0.##}%"
                    });

                    usedDate.Add(date);
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
                Memo = "P=PREDAYHIGH; previous 5m candle above P supports MA5/MA10, current candle crosses previous high, closes above P/O, RSI2>50. No live orders."
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

            for (int i = 1; i < sorted.Count; i++)
            {
                string date = BacktestDataStore.NormalizeDate(sorted[i].Date);
                BacktestDailyBar previous = sorted[i - 1];
                result[date] = new PreviousDayState(previous.High, previous.Low, previous.Close);
            }

            return result;
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

        private static Dictionary<int, decimal> BuildRsiStates(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new Dictionary<int, decimal>();
            if (bars.Count <= period)
                return result;

            for (int i = period; i < bars.Count; i++)
            {
                decimal gains = 0m;
                decimal losses = 0m;
                for (int j = i - period + 1; j <= i; j++)
                {
                    decimal change = bars[j].Close - bars[j - 1].Close;
                    if (change > 0)
                        gains += change;
                    else
                        losses += Math.Abs(change);
                }

                decimal averageGain = gains / period;
                decimal averageLoss = losses / period;
                result[i] = averageLoss <= 0m
                    ? 100m
                    : 100m - 100m / (1m + averageGain / averageLoss);
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
        private readonly record struct PreviousDayState(long High, long Low, long Close);
        private readonly record struct MaState(decimal Ma5, decimal Ma10);
    }
}
