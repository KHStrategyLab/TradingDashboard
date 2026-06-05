using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class DailyBaseTenMinuteMa60RecoverBacktest
    {
        public const string StrategyCode = "DAILY_500EOK_20P_BASE_10M_MA60_RECOVER";
        public const string ExitRuleCode = "ONE_SWING_10M_MA5_BREAK_OR_MA60_FAIL";

        private const long MinDailyTradingValueWon = 50_000_000_000;
        private const decimal MinDailyChangeRate = 20m;
        private const int TrackingTradingDays = 6;

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;
        private readonly int _minuteInterval;
        private readonly string _strategyCode;
        private readonly string _exitRuleCode;

        public DailyBaseTenMinuteMa60RecoverBacktest(
            int minuteInterval = 10,
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _minuteInterval = minuteInterval == 15 ? 15 : 10;
            _strategyCode = _minuteInterval == 15
                ? "DAILY_500EOK_20P_BASE_15M_MA60_RECOVER"
                : StrategyCode;
            _exitRuleCode = _minuteInterval == 15
                ? "ONE_SWING_15M_MA5_BREAK_OR_MA60_FAIL"
                : ExitRuleCode;
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run()
        {
            string runId = _runStore.CreateRunId($"daily_500eok_20p_base_{_minuteInterval}m_ma60_recover");
            List<BacktestSignalRow> signals = [];
            List<BacktestTradeRow> trades = [];

            foreach (StockMarketKey stock in EnumerateStockMarkets())
            {
                List<BacktestDailyBar> dailyBars = [.. _dataStore.LoadDailyBars(stock.Code, stock.Market)
                    .OrderBy(bar => BacktestDataStore.NormalizeDate(bar.Date))];
                List<BacktestMinuteBar> minuteBars = [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, _minuteInterval)
                    .OrderBy(bar => bar.DateTime)];

                if (dailyBars.Count < 2 || minuteBars.Count < 80)
                    continue;

                Dictionary<string, int> dailyIndexByDate = dailyBars
                    .Select((bar, index) => new { Date = BacktestDataStore.NormalizeDate(bar.Date), Index = index })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Date))
                    .GroupBy(item => item.Date, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);
                Dictionary<string, MaState> minuteMa60ByTime = BuildMaStateMap(minuteBars, 60);
                HashSet<string> usedBaseKeys = new(StringComparer.Ordinal);

                for (int dayIndex = 0; dayIndex < dailyBars.Count; dayIndex++)
                {
                    BacktestDailyBar baseDaily = dailyBars[dayIndex];
                    string baseDate = BacktestDataStore.NormalizeDate(baseDaily.Date);
                    if (string.IsNullOrWhiteSpace(baseDate))
                        continue;

                    long dailyTradingValue = ResolveTradingValue(baseDaily);
                    bool dailyBase =
                        baseDaily.Close > baseDaily.Open &&
                        baseDaily.ChangeRate >= MinDailyChangeRate &&
                        dailyTradingValue >= MinDailyTradingValueWon;
                    if (!dailyBase)
                        continue;

                    string baseKey = $"{stock.Code}|{stock.Market}|{baseDate}";
                    if (!usedBaseKeys.Add(baseKey))
                        continue;

                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = _strategyCode,
                        Code = stock.Code,
                        Market = stock.Market,
                        SignalTime = $"{baseDate}000000",
                        SignalType = "DAILY_BASE",
                        Price = baseDaily.Close,
                        Reason = $"daily base candle: value {dailyTradingValue / 100_000_000m:0.##}eok >=500eok; change {baseDaily.ChangeRate:0.##}% >=20%; open {baseDaily.Open:N0}; close {baseDaily.Close:N0}; low {baseDaily.Low:N0}"
                    });

                    if (!TryFindTenMinuteRecoveryTrade(
                        stock,
                        dailyBars,
                        dailyIndexByDate,
                        minuteBars,
                        minuteMa60ByTime,
                        dayIndex,
                        baseDaily,
                        out EntryExitResult trade))
                    {
                        continue;
                    }

                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = _strategyCode,
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
                        StrategyCode = _strategyCode,
                        Code = stock.Code,
                        Market = stock.Market,
                        SignalTime = trade.ExitTime,
                        SignalType = "SELL",
                        Price = trade.ExitPrice,
                        Reason = trade.ExitReason
                    });
                    trades.Add(BuildTradeRow(runId, stock, trade));
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
                MarketMode = "AL_ONLY",
                OrderMode = "None",
                LiveOrder = false,
                ExecutionType = "BacktestOnly",
                Memo = $"Read-only test. Daily base candle requires value >=500eok, change >=20%, bullish candle. After the daily base candle, look for a {_minuteInterval}m close recovering above MA60 within the next 6 trading days. Exit watches only the first {_minuteInterval}m swing: MA60 or signal-low failure is a structural stop, and the first close below MA5 after a positive push ends the swing."
            };

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries, config);
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries[0]);
        }

        private bool TryFindTenMinuteRecoveryTrade(
            StockMarketKey stock,
            IReadOnlyList<BacktestDailyBar> dailyBars,
            IReadOnlyDictionary<string, int> dailyIndexByDate,
            IReadOnlyList<BacktestMinuteBar> tenBars,
            IReadOnlyDictionary<string, MaState> tenMa60ByTime,
            int baseDayIndex,
            BacktestDailyBar baseDaily,
            out EntryExitResult result)
        {
            result = default;
            string baseDate = BacktestDataStore.NormalizeDate(baseDaily.Date);
            int lastDayIndex = Math.Min(dailyBars.Count - 1, baseDayIndex + TrackingTradingDays);
            HashSet<string> trackingDates = [.. dailyBars
                .Skip(baseDayIndex + 1)
                .Take(Math.Max(0, lastDayIndex - baseDayIndex))
                .Select(bar => BacktestDataStore.NormalizeDate(bar.Date))
                .Where(date => !string.IsNullOrWhiteSpace(date))];

            if (trackingDates.Count == 0)
                return false;

            for (int i = 1; i < tenBars.Count; i++)
            {
                BacktestMinuteBar previous = tenBars[i - 1];
                BacktestMinuteBar current = tenBars[i];
                string currentDate = ResolveDate(current.DateTime);
                if (!trackingDates.Contains(currentDate))
                    continue;

                if (!dailyIndexByDate.TryGetValue(currentDate, out int currentDayIndex) ||
                    currentDayIndex <= baseDayIndex ||
                    currentDayIndex > lastDayIndex)
                {
                    continue;
                }

                if (!tenMa60ByTime.TryGetValue(previous.DateTime, out MaState previousMa) ||
                    !tenMa60ByTime.TryGetValue(current.DateTime, out MaState currentMa) ||
                    previousMa.Ma <= 0 ||
                    currentMa.Ma <= 0)
                {
                    continue;
                }

                bool recovered = previous.Close <= previousMa.Ma && current.Close > currentMa.Ma;
                bool survivedBaseLow = current.Close > baseDaily.Low;
                if (!recovered || !survivedBaseLow)
                    continue;

                string entryTime = current.DateTime;
                long entryPrice = current.Close;
                if (!TryResolveOneSwingExit(tenBars, tenMa60ByTime, i, entryTime, entryPrice, current.Low, baseDaily.Low, out ExitResult exit))
                    return false;

                result = new EntryExitResult(
                    entryTime,
                    entryPrice,
                    exit.ExitTime,
                    exit.ExitPrice,
                    exit.MaxHigh,
                    exit.MinLow,
                    baseDaily.Low,
                    exit.HoldingMinutes,
                    $"{_minuteInterval}m MA60 recovery after daily base {baseDate}; base value {ResolveTradingValue(baseDaily) / 100_000_000m:0.##}eok; base change {baseDaily.ChangeRate:0.##}%; base low {baseDaily.Low:N0}; {_minuteInterval}m MA60 {currentMa.Ma:N0}; previous close {previous.Close:N0}; entry close {current.Close:N0}",
                    exit.ExitReason);
                return true;
            }

            return false;
        }

        private bool TryResolveOneSwingExit(
            IReadOnlyList<BacktestMinuteBar> bars,
            IReadOnlyDictionary<string, MaState> ma60ByTime,
            int entryIndex,
            string entryTime,
            long entryPrice,
            long signalLow,
            long stopPrice,
            out ExitResult result)
        {
            result = default;
            long maxHigh = entryPrice;
            long minLow = entryPrice;
            decimal positivePushThreshold = entryPrice * 1.002m;
            int maxSwingBars = _minuteInterval == 15 ? 12 : 18;

            for (int i = entryIndex + 1; i < bars.Count && i <= entryIndex + maxSwingBars; i++)
            {
                BacktestMinuteBar current = bars[i];
                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                decimal ma5 = i >= 4 ? bars.Skip(i - 4).Take(5).Average(bar => (decimal)bar.Close) : 0m;
                decimal ma60 = ma60ByTime.TryGetValue(current.DateTime, out MaState ma60State) ? ma60State.Ma : 0m;
                bool hasPositivePush = maxHigh >= positivePushThreshold;
                bool signalLowFailed = current.Close < signalLow;
                bool ma60Failed = ma60 > 0 && current.Close < ma60;
                bool ma5BreakAfterPush = hasPositivePush && ma5 > 0 && current.Close < ma5;

                if (!signalLowFailed && !ma60Failed && !ma5BreakAfterPush)
                {
                    continue;
                }

                int holdingMinutes = ResolveHoldingMinutes(entryTime, current.DateTime, i - entryIndex);
                decimal mfe = entryPrice > 0 ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m : 0m;
                decimal mae = entryPrice > 0 ? (minLow - entryPrice) / (decimal)entryPrice * 100m : 0m;
                string reason = signalLowFailed
                    ? $"{_minuteInterval}m signal-low close failure"
                    : ma60Failed
                        ? $"{_minuteInterval}m MA60 close failure"
                        : $"first {_minuteInterval}m MA5 close break after positive push";
                result = new ExitResult(
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    holdingMinutes,
                    $"{reason}; maxHigh {maxHigh:N0}; high move {mfe:0.##}%; low move {mae:0.##}%; signalLow {signalLow:N0}; ma5 {ma5:N0}; ma60 {ma60:N0}; dailyBaseLowStop {stopPrice:N0}");
                return true;
            }

            BacktestMinuteBar fallback = bars[Math.Min(bars.Count - 1, entryIndex + maxSwingBars)];
            maxHigh = Math.Max(maxHigh, fallback.High);
            minLow = Math.Min(minLow, fallback.Low);
            int fallbackHoldingMinutes = ResolveHoldingMinutes(entryTime, fallback.DateTime, Math.Min(maxSwingBars, Math.Max(1, bars.Count - 1 - entryIndex)));
            decimal fallbackMfe = entryPrice > 0 ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m : 0m;
            decimal fallbackMae = entryPrice > 0 ? (minLow - entryPrice) / (decimal)entryPrice * 100m : 0m;
            result = new ExitResult(
                fallback.DateTime,
                fallback.Close,
                maxHigh,
                minLow,
                fallbackHoldingMinutes,
                $"one-swing max {maxSwingBars} bars timeout; maxHigh {maxHigh:N0}; high move {fallbackMfe:0.##}%; low move {fallbackMae:0.##}%; signalLow {signalLow:N0}; dailyBaseLowStop {stopPrice:N0}");
            return true;
        }

        private IEnumerable<StockMarketKey> EnumerateStockMarkets()
        {
            string directory = Path.Combine(_dataStore.RootPath, "minute", $"{_minuteInterval}m");
            if (!Directory.Exists(directory))
                yield break;

            var stocks = new List<StockMarketKey>();
            foreach (string path in Directory.EnumerateFiles(directory, $"*_{_minuteInterval}m.json").OrderBy(item => item, StringComparer.Ordinal))
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

            bool hasAl = stocks.Any(stock => stock.Market == "AL");
            foreach (StockMarketKey stock in stocks.Where(stock => !hasAl || stock.Market == "AL"))
                yield return stock;
        }

        private static Dictionary<string, MaState> BuildMaStateMap(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new Dictionary<string, MaState>(StringComparer.Ordinal);
            int resolved = Math.Max(2, period);
            for (int i = resolved - 1; i < bars.Count; i++)
            {
                BacktestMinuteBar current = bars[i];
                decimal ma = bars.Skip(i - resolved + 1).Take(resolved).Average(bar => (decimal)bar.Close);
                result[current.DateTime] = new MaState(current.DateTime, current.Close, ma);
            }

            return result;
        }

        private BacktestTradeRow BuildTradeRow(string runId, StockMarketKey stock, EntryExitResult trade)
        {
            decimal profitRate = trade.EntryPrice > 0 ? (trade.ExitPrice - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m : 0m;
            decimal mae = trade.EntryPrice > 0 ? (trade.MinLow - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m : 0m;
            decimal mfe = trade.EntryPrice > 0 ? (trade.MaxHigh - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m : 0m;
            long riskWon = trade.EntryPrice - trade.StopPrice;
            decimal riskRate = trade.EntryPrice > 0 && riskWon > 0 ? riskWon / (decimal)trade.EntryPrice * 100m : 0m;

            return new BacktestTradeRow
            {
                RunId = runId,
                StrategyCode = _strategyCode,
                ExitRuleCode = _exitRuleCode,
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
                MaxR = riskWon > 0 ? (trade.MaxHigh - trade.EntryPrice) / (decimal)riskWon : 0m,
                MinR = riskWon > 0 ? (trade.MinLow - trade.EntryPrice) / (decimal)riskWon : 0m,
                HoldingMinutes = trade.HoldingMinutes,
                EntryReason = trade.EntryReason,
                ExitReason = trade.ExitReason
            };
        }

        private BacktestRunSummary BuildSummary(string runId, string market, IReadOnlyList<BacktestSignalRow> signals, IReadOnlyList<BacktestTradeRow> trades)
        {
            int wins = trades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. trades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. trades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];
            return new BacktestRunSummary
            {
                RunId = runId,
                Market = market,
                StrategyCode = _strategyCode,
                ExitRuleCode = _exitRuleCode,
                SignalCount = signals.Count,
                TradeCount = trades.Count,
                WinRate = trades.Count > 0 ? wins / (decimal)trades.Count * 100m : 0m,
                AvgProfit = profits.Count > 0 ? profits.Average() : 0m,
                AvgLoss = losses.Count > 0 ? losses.Average() : 0m,
                Expectancy = trades.Count > 0 ? trades.Average(trade => trade.ProfitRate) : 0m,
                TotalProfit = trades.Sum(trade => trade.ProfitAmount),
                MAE = trades.Count > 0 ? trades.Average(trade => trade.Mae) : 0m,
                MFE = trades.Count > 0 ? trades.Average(trade => trade.Mfe) : 0m,
                AvgHoldingMinutes = trades.Count > 0 ? (decimal)trades.Average(trade => trade.HoldingMinutes) : 0m,
                ConsecutiveLosses = ResolveMaxConsecutiveLosses(trades)
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

        private static long ResolveTradingValue(BacktestDailyBar bar)
        {
            long estimated = bar.Volume > 0 && bar.Close > 0 && bar.Volume <= long.MaxValue / Math.Max(1, bar.Close)
                ? bar.Volume * bar.Close
                : 0;
            if (bar.TradingValue <= 0)
                return estimated;

            long storedAsMillionWon = bar.TradingValue > long.MaxValue / 1_000_000
                ? bar.TradingValue
                : bar.TradingValue * 1_000_000;
            if (estimated <= 0)
                return Math.Max(bar.TradingValue, storedAsMillionWon);

            long millionWonDistance = Math.Abs(storedAsMillionWon - estimated);
            long rawDistance = Math.Abs(bar.TradingValue - estimated);
            return millionWonDistance <= rawDistance ? storedAsMillionWon : Math.Max(bar.TradingValue, estimated);
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

        private static int ResolveHoldingMinutes(string entryTime, string exitTime, int fallbackBars)
        {
            if (DateTime.TryParseExact(entryTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entry) &&
                DateTime.TryParseExact(exitTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exit))
            {
                return Math.Max(1, (int)Math.Round((exit - entry).TotalMinutes));
            }

            return Math.Max(1, fallbackBars * 10);
        }

        private readonly record struct StockMarketKey(string Code, string Market);
        private readonly record struct MaState(string Time, long Close, decimal Ma);
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
        private readonly record struct ExitResult(
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            int HoldingMinutes,
            string ExitReason);
    }
}
