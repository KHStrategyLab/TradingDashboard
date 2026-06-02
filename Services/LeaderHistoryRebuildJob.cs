using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TradingDashboard.Models;
using TradingDashboard.Services.Backtests;

namespace TradingDashboard.Services
{
    public sealed class LeaderHistoryRebuildJob
    {
        private const long MinTradingValue = 100_000_000_000;
        private const decimal MinChangeRate = 25m;
        private const long LargeTradingValue = 300_000_000_000;
        private const decimal LargeTradeMinChangeRate = 20m;

        private readonly BacktestDataStore _dataStore;
        private readonly LeaderHistoryStore _store;
        private readonly LeaderHistoryQualityScorer _scorer;

        public LeaderHistoryRebuildJob(
            BacktestDataStore? dataStore = null,
            LeaderHistoryStore? store = null,
            LeaderHistoryQualityScorer? scorer = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _store = store ?? new LeaderHistoryStore();
            _scorer = scorer ?? new LeaderHistoryQualityScorer();
        }

        public LeaderHistoryRebuildSummary Rebuild(int lookbackTradingDays = 6)
        {
            int resolvedLookback = Math.Max(1, lookbackTradingDays);
            string runId = DateTime.Now.ToString("yyyyMMddHHmmss");
            List<DailySeries> dailySeries = LoadPreferredDailySeries(_dataStore);
            Dictionary<string, string> stockNameByCode = LoadStockNameByCode();
            Dictionary<string, string> nameByCodeDate = LoadNameByCodeDate(_dataStore.LoadBaseCandles());
            Dictionary<string, long> krxCloseByCodeDate = LoadCloseByCodeDate(_dataStore, "KRX");
            Dictionary<string, long> nxtCloseByCodeDate = LoadNxtCloseByCodeDate(_dataStore);
            List<BacktestDailyBar> allBars = [.. dailySeries.SelectMany(series => series.Bars)];
            List<string> recentDates = [.. allBars
                .Select(bar => BacktestDataStore.NormalizeDate(bar.Date))
                .Where(date => !string.IsNullOrWhiteSpace(date))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(date => date)
                .TakeLast(resolvedLookback)];

            string startDate = recentDates.FirstOrDefault() ?? string.Empty;
            string endDate = recentDates.LastOrDefault() ?? string.Empty;
            List<LeaderHistoryEntry> leaders = [];

            foreach (DailySeries series in dailySeries)
            {
                for (int i = 0; i < series.Bars.Count; i++)
                {
                    BacktestDailyBar bar = series.Bars[i];
                    string date = BacktestDataStore.NormalizeDate(bar.Date);
                    if (!recentDates.Contains(date, StringComparer.Ordinal))
                        continue;

                    long tradingValue = ResolveTradingValue(bar);
                    decimal changeRate = ResolveChangeRate(bar);
                    if (!PassesPowerGate(tradingValue, changeRate))
                        continue;

                    BacktestDailyBar? previous = i > 0 ? series.Bars[i - 1] : null;
                    string market = BacktestDataStore.NormalizeMarket(series.Market);
                    LeaderHistoryEntry entry = new()
                    {
                        Key = $"{series.Code}|{market}|{date}",
                        Code = series.Code,
                        Name = ResolveName(series, date, nameByCodeDate, stockNameByCode),
                        Market = market,
                        BaseDate = date,
                        Open = bar.Open,
                        High = bar.High,
                        Low = bar.Low,
                        Close = bar.Close,
                        Volume = bar.Volume,
                        TradingValue = tradingValue,
                        ChangeRate = changeRate,
                        DailyRsi14 = ResolveRsi14(series.Bars, i),
                        CloseLocationPercent = ResolveCloseLocationPercent(bar),
                        UpperTailPercent = ResolveUpperTailPercent(bar),
                        KrxClose = string.Equals(market, "KRX", StringComparison.Ordinal)
                            ? bar.Close
                            : krxCloseByCodeDate.TryGetValue($"{series.Code}|{date}", out long krxClose) ? krxClose : 0,
                        NxtClose = string.Equals(market, "NXT", StringComparison.Ordinal)
                            ? bar.Close
                            : nxtCloseByCodeDate.TryGetValue($"{series.Code}|{date}", out long nxtClose) ? nxtClose : 0,
                        BollingerUpperBreak = ResolveBollingerUpperBreak(series.Bars, i),
                        PrevHighPlus10 = previous != null && previous.High > 0 && bar.Close >= previous.High * 1.10m,
                        Source = "Rebuild",
                        Status = "Active",
                        SavedAt = runId,
                        ExpiresAfterTradingDays = resolvedLookback
                    };

                    leaders.Add(_scorer.Score(entry));
                }
            }

            leaders = [.. leaders
                .Where(item => !item.ManualDiscarded)
                .OrderByDescending(item => item.QualityScore)
                .ThenByDescending(item => item.TradingValue)
                .ThenBy(item => item.Code)];

            _store.SaveActive(leaders);
            _store.SaveArchiveSnapshot(leaders, runId);

            var summary = new LeaderHistoryRebuildSummary
            {
                RunId = runId,
                LookbackTradingDays = resolvedLookback,
                StartDate = startDate,
                EndDate = endDate,
                DailyBarCount = allBars.Count,
                CandidateCount = leaders.Count,
                ActiveLeaderCount = leaders.Count,
                GradeCounts = leaders
                    .GroupBy(item => item.QualityGrade, StringComparer.Ordinal)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                LeaderTypeCounts = leaders
                    .GroupBy(item => item.LeaderType, StringComparer.Ordinal)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                Logs =
                [
                    $"leader history rebuilt: {leaders.Count}leaders / {startDate}-{endDate}",
                    $"active path: {_store.ActivePath}",
                    "active csv: Storage/LeaderHistory/active_leaders.csv",
                    "NXT daily series is preferred when available; KRX is used as fallback",
                    "market cap and turnover are reserved fields; missing values do not block rebuild"
                ]
            };
            _store.SaveSummary(summary);
            return summary;
        }

        private static List<DailySeries> LoadPreferredDailySeries(BacktestDataStore dataStore)
        {
            string dailyDirectory = Path.Combine(dataStore.RootPath, "daily");
            if (!Directory.Exists(dailyDirectory))
                return [];

            var resultByCode = new Dictionary<string, DailySeries>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(dailyDirectory, "*_*_daily.json", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                string[] parts = fileName.Split('_', StringSplitOptions.RemoveEmptyEntries);
                string code = BacktestDataStore.NormalizeCode(parts.FirstOrDefault() ?? string.Empty);
                string market = parts.Length >= 2 ? BacktestDataStore.NormalizeMarket(parts[1]) : "KRX";
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                List<BacktestDailyBar> bars = [.. dataStore.LoadDailyBars(code, market)
                    .Where(bar => bar != null && !string.IsNullOrWhiteSpace(bar.Date))
                    .OrderBy(bar => BacktestDataStore.NormalizeDate(bar.Date))];
                if (bars.Count == 0)
                    continue;

                if (!resultByCode.TryGetValue(code, out DailySeries? existing) ||
                    ShouldPreferMarket(market, existing.Market))
                {
                    resultByCode[code] = new DailySeries(code, market, string.Empty, bars);
                }
            }

            return [.. resultByCode.Values
                .OrderBy(series => series.Code)];
        }

        private static Dictionary<string, string> LoadNameByCodeDate(IEnumerable<BacktestBaseCandle> baseCandles)
        {
            return (baseCandles ?? [])
                .Where(item => item != null &&
                    !string.IsNullOrWhiteSpace(item.Code) &&
                    !string.IsNullOrWhiteSpace(item.BaseCandleDate) &&
                    !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => $"{BacktestDataStore.NormalizeCode(item.Code)}|{BacktestDataStore.NormalizeDate(item.BaseCandleDate)}", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
        }

        private static Dictionary<string, string> LoadStockNameByCode()
        {
            try
            {
                StockMasterCacheDocument? document = new StockMasterCacheStore()
                    .LoadAsync()
                    .GetAwaiter()
                    .GetResult();
                if (document?.Items is not { Count: > 0 })
                    return new Dictionary<string, string>(StringComparer.Ordinal);

                return document.Items
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.Name))
                    .GroupBy(item => BacktestDataStore.NormalizeCode(item.Code), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static Dictionary<string, long> LoadNxtCloseByCodeDate(BacktestDataStore dataStore)
        {
            return LoadCloseByCodeDate(dataStore, "NXT");
        }

        private static Dictionary<string, long> LoadCloseByCodeDate(BacktestDataStore dataStore, string market)
        {
            string dailyDirectory = Path.Combine(dataStore.RootPath, "daily");
            if (!Directory.Exists(dailyDirectory))
                return new Dictionary<string, long>(StringComparer.Ordinal);

            string normalizedMarket = BacktestDataStore.NormalizeMarket(market);
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(dailyDirectory, $"*_{normalizedMarket}_daily.json", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                string code = BacktestDataStore.NormalizeCode(fileName.Split('_').FirstOrDefault() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                foreach (BacktestDailyBar bar in dataStore.LoadDailyBars(code, normalizedMarket))
                {
                    string date = BacktestDataStore.NormalizeDate(bar.Date);
                    if (!string.IsNullOrWhiteSpace(date))
                        result[$"{code}|{date}"] = bar.Close;
                }
            }

            return result;
        }

        private static bool ShouldPreferMarket(string candidateMarket, string currentMarket)
        {
            string candidate = BacktestDataStore.NormalizeMarket(candidateMarket);
            string current = BacktestDataStore.NormalizeMarket(currentMarket);
            return string.Equals(candidate, "NXT", StringComparison.Ordinal) &&
                !string.Equals(current, "NXT", StringComparison.Ordinal);
        }

        private static bool PassesPowerGate(long tradingValue, decimal changeRate)
        {
            return (tradingValue >= MinTradingValue && changeRate >= MinChangeRate) ||
                (tradingValue >= LargeTradingValue && changeRate >= LargeTradeMinChangeRate);
        }

        private static string ResolveName(
            DailySeries series,
            string date,
            IReadOnlyDictionary<string, string> nameByCodeDate,
            IReadOnlyDictionary<string, string> stockNameByCode)
        {
            if (!string.IsNullOrWhiteSpace(series.Name))
                return series.Name;

            if (nameByCodeDate.TryGetValue($"{series.Code}|{date}", out string? baseName) &&
                !string.Equals(baseName, series.Code, StringComparison.Ordinal))
            {
                return baseName;
            }

            return stockNameByCode.TryGetValue(series.Code, out string? stockName) ? stockName : string.Empty;
        }

        private static long ResolveTradingValue(BacktestDailyBar bar)
        {
            long estimated = bar.Close > 0 && bar.Volume > 0
                ? (long)Math.Min(long.MaxValue, bar.Close * (double)bar.Volume)
                : 0;
            return Math.Max(bar.TradingValue, estimated);
        }

        private static decimal ResolveChangeRate(BacktestDailyBar bar)
        {
            if (bar.ChangeRate != 0 || bar.PreviousClose <= 0)
                return bar.ChangeRate;

            return (bar.Close - bar.PreviousClose) / (decimal)bar.PreviousClose * 100m;
        }

        private static decimal ResolveCloseLocationPercent(BacktestDailyBar bar)
        {
            long range = bar.High - bar.Low;
            if (range <= 0)
                return 0m;

            return (bar.Close - bar.Low) / (decimal)range * 100m;
        }

        private static decimal ResolveUpperTailPercent(BacktestDailyBar bar)
        {
            long range = bar.High - bar.Low;
            if (range <= 0)
                return 0m;

            long bodyHigh = Math.Max(bar.Open, bar.Close);
            return (bar.High - bodyHigh) / (decimal)range * 100m;
        }

        private static bool ResolveBollingerUpperBreak(IReadOnlyList<BacktestDailyBar> bars, int index)
        {
            const int period = 20;
            if (index < period - 1)
                return false;

            List<decimal> closes = [.. bars
                .Skip(index - period + 1)
                .Take(period)
                .Select(bar => (decimal)bar.Close)];
            decimal average = closes.Average();
            decimal variance = closes.Sum(close => (close - average) * (close - average)) / period;
            decimal stdDev = (decimal)Math.Sqrt((double)variance);
            decimal upper = average + stdDev * 2m;
            return bars[index].Close > upper;
        }

        private static decimal? ResolveRsi14(IReadOnlyList<BacktestDailyBar> bars, int index)
        {
            const int period = 14;
            if (bars == null || index < period || index >= bars.Count)
                return null;

            decimal gainSum = 0m;
            decimal lossSum = 0m;
            for (int i = index - period + 1; i <= index; i++)
            {
                long previousClose = bars[i - 1].Close;
                long close = bars[i].Close;
                decimal change = close - previousClose;
                if (change > 0)
                    gainSum += change;
                else
                    lossSum += Math.Abs(change);
            }

            decimal avgGain = gainSum / period;
            decimal avgLoss = lossSum / period;
            if (avgLoss == 0m)
                return avgGain == 0m ? 50m : 100m;

            decimal rs = avgGain / avgLoss;
            return Math.Round(100m - (100m / (1m + rs)), 2);
        }

        private static string ResolveProjectRoot()
        {
            string? fromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(fromCurrent))
                return Directory.GetParent(fromCurrent)?.FullName ?? Directory.GetCurrentDirectory();

            string? fromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(fromBase))
                return Directory.GetParent(fromBase)?.FullName ?? AppContext.BaseDirectory;

            return Directory.GetCurrentDirectory();
        }

        private static string? SearchUpwards(string startDirectory, string childDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, childDirectory);
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }

            return null;
        }

        private sealed record DailySeries(string Code, string Market, string Name, List<BacktestDailyBar> Bars);
    }
}
