using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class PrevHighFirstPullbackBreakoutBacktest
    {
        public const string StrategyCode = "PREDAY_HIGH_FIRST_PULLBACK_BREAKOUT";
        public const string ExitRuleCode = "OBSERVE_180M_CLOSE";

        private const int Minute = 5;
        private const int ObservationMinutes = 180;
        private const int Ma5Period = 5;
        private const int Ma10Period = 10;

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;

        public PrevHighFirstPullbackBreakoutBacktest(
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run()
        {
            string runId = _runStore.CreateRunId("preday_high_first_pullback_breakout_5m_observe180");
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
                var usedDate = new HashSet<string>(StringComparer.Ordinal);

                string currentDate = string.Empty;
                StageState stage = StageState.None;
                long previousHigh = 0;
                long breakHigh = 0;
                long pullbackHigh = 0;
                long pullbackLow = 0;
                string breakTime = string.Empty;
                string pullbackTime = string.Empty;

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
                        stage = StageState.None;
                        breakHigh = 0;
                        pullbackHigh = 0;
                        pullbackLow = 0;
                        breakTime = string.Empty;
                        pullbackTime = string.Empty;
                        previousHigh = previousDayByDate.TryGetValue(date, out PreviousDayState previousDay)
                            ? previousDay.High
                            : 0;
                    }

                    if (previousHigh <= 0 || usedDate.Contains(date))
                        continue;

                    if (!maByIndex.TryGetValue(i, out MaState ma))
                        continue;

                    if (stage == StageState.None)
                    {
                        bool crossesAbovePreviousHigh =
                            previous.Close <= previousHigh &&
                            current.Close > previousHigh;

                        if (crossesAbovePreviousHigh)
                        {
                            stage = StageState.PrevHighBreak;
                            breakHigh = current.High;
                            breakTime = current.DateTime;
                            signals.Add(BuildSignal(runId, stock, current, "STAGE_PREV_HIGH_BREAK",
                                $"PrevHighBreak; previousHigh {previousHigh:N0}; breakHigh {breakHigh:N0}; value {current.TradingValue / 100_000_000m:0.##}eok"));
                        }

                        continue;
                    }

                    if (stage == StageState.PrevHighBreak)
                    {
                        bool ma5Support = current.Low <= ma.Ma5 && current.Close >= ma.Ma5;
                        bool ma10Support = current.Low <= ma.Ma10 && current.Close >= ma.Ma10;
                        if (ma5Support || ma10Support)
                        {
                            stage = StageState.FirstPullbackSupported;
                            pullbackHigh = current.High;
                            pullbackLow = current.Low;
                            pullbackTime = current.DateTime;
                            signals.Add(BuildSignal(runId, stock, current, "STAGE_FIRST_PULLBACK_SUPPORTED",
                                $"FirstPullbackSupported; previousHigh {previousHigh:N0}; breakHigh {breakHigh:N0}; pullbackHigh {pullbackHigh:N0}; pullbackLow {pullbackLow:N0}; MA5 {ma.Ma5:N0}; MA10 {ma.Ma10:N0}; support {(ma5Support ? "MA5" : "MA10")}; value {current.TradingValue / 100_000_000m:0.##}eok"));
                        }

                        continue;
                    }

                    if (stage == StageState.FirstPullbackSupported)
                    {
                        bool buySignal = current.Close > pullbackHigh && current.Close > previousHigh;
                        if (!buySignal)
                            continue;

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
                        int holdingMinutes = ResolveHoldingMinutes(current.DateTime, exitBar.DateTime, holding.Count * Minute);
                        string reason =
                            $"BUY; PrevHighFirstPullbackBreakout; previousHigh {previousHigh:N0}; breakTime {breakTime}; breakHigh {breakHigh:N0}; pullbackTime {pullbackTime}; pullbackHigh {pullbackHigh:N0}; pullbackLow {pullbackLow:N0}; entry {entryPrice:N0}; value {current.TradingValue / 100_000_000m:0.##}eok";

                        signals.Add(BuildSignal(runId, stock, current, "BUY", reason));
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
                            StopPrice = pullbackLow,
                            Quantity = 1,
                            ProfitRate = profitRate,
                            ProfitAmount = exitBar.Close - entryPrice,
                            Mae = mae,
                            Mfe = mfe,
                            RiskRate = entryPrice > 0 ? (entryPrice - pullbackLow) / (decimal)entryPrice * 100m : 0m,
                            MaxR = 0,
                            MinR = 0,
                            HoldingMinutes = holdingMinutes,
                            EntryReason = reason,
                            ExitReason = $"observe {ObservationMinutes}m close only; maxHigh MFE {mfe:0.##}%, minLow MAE {mae:0.##}%"
                        });

                        usedDate.Add(date);
                    }
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
                Memo = "P=PREDAYHIGH; after close cross above P, wait first MA5/MA10 pullback support, then buy when close breaks pullback high and remains above P. No live orders."
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

        private enum StageState
        {
            None,
            PrevHighBreak,
            FirstPullbackSupported
        }

        private sealed record StockMarketKey(string Code, string Market);
        private readonly record struct PreviousDayState(long High, long Low, long Close);
        private readonly record struct MaState(decimal Ma5, decimal Ma10);
    }
}
