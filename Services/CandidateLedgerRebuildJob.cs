using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TradingDashboard.Models;
using TradingDashboard.Services.Backtests;

namespace TradingDashboard.Services
{
    public sealed class CandidateLedgerRebuildJob
    {
        private const long DefaultMinTradingValue = 100_000_000_000;
        private const decimal DefaultMinChangeRate = 25m;

        private readonly BacktestDataStore _dataStore;
        private readonly CandidateLedgerStore _store;

        public CandidateLedgerRebuildJob(
            BacktestDataStore? dataStore = null,
            CandidateLedgerStore? store = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
            _store = store ?? new CandidateLedgerStore();
        }

        public CandidateLedgerRebuildSummary Rebuild(
            int lookbackTradingDays = 6,
            long minTradingValue = DefaultMinTradingValue,
            decimal minChangeRate = DefaultMinChangeRate)
        {
            int resolvedLookback = Math.Max(1, lookbackTradingDays);
            string runId = DateTime.Now.ToString("yyyyMMddHHmmss");
            Dictionary<string, string> stockNameByCode = LoadStockNameByCode();
            List<DailySeries> seriesList = LoadKrxDailySeries(_dataStore);
            List<BacktestDailyBar> allBars = [.. seriesList.SelectMany(series => series.Bars)];
            List<string> recentDates = [.. allBars
                .Select(bar => BacktestDataStore.NormalizeDate(bar.Date))
                .Where(date => !string.IsNullOrWhiteSpace(date))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(date => date)
                .TakeLast(resolvedLookback)];

            string startDate = recentDates.FirstOrDefault() ?? string.Empty;
            string endDate = recentDates.LastOrDefault() ?? string.Empty;
            Dictionary<string, CandidateLedgerEntry> merged = _store.LoadActive()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Key))
                .ToDictionary(item => item.Key, item => item, StringComparer.Ordinal);

            foreach (DailySeries series in seriesList)
            {
                for (int i = 0; i < series.Bars.Count; i++)
                {
                    BacktestDailyBar today = series.Bars[i];
                    string date = BacktestDataStore.NormalizeDate(today.Date);
                    if (!recentDates.Contains(date, StringComparer.Ordinal))
                        continue;

                    long tradingValue = ResolveTradingValue(today);
                    decimal changeRate = ResolveChangeRate(today);
                    if (tradingValue < minTradingValue || changeRate < minChangeRate)
                        continue;

                    BacktestDailyBar? previous = i > 0 ? series.Bars[i - 1] : null;
                    CandidateLedgerEntry entry = BuildEntry(
                        series,
                        today,
                        previous,
                        i,
                        runId,
                        stockNameByCode);

                    merged[entry.Key] = entry;
                }
            }

            List<CandidateLedgerEntry> active = [.. merged.Values
                .Where(item => recentDates.Contains(BacktestDataStore.NormalizeDate(item.CandidateDate), StringComparer.Ordinal))
                .OrderByDescending(item => item.CandidateDate)
                .ThenByDescending(item => item.TradingValue)
                .ThenBy(item => item.Code)];

            List<CandidateLedgerEntry> archive = [.. merged.Values
                .Where(item => !recentDates.Contains(BacktestDataStore.NormalizeDate(item.CandidateDate), StringComparer.Ordinal))
                .OrderByDescending(item => item.CandidateDate)
                .ThenBy(item => item.Code)];

            _store.SaveActive(active);
            if (archive.Count > 0)
                _store.SaveArchiveSnapshot(archive, runId);

            var summary = new CandidateLedgerRebuildSummary
            {
                RunId = runId,
                LookbackTradingDays = resolvedLookback,
                StartDate = startDate,
                EndDate = endDate,
                DailyBarCount = allBars.Count,
                CandidateCount = active.Count + archive.Count,
                ActiveCandidateCount = active.Count,
                ArchiveCandidateCount = archive.Count,
                SourceCounts = active
                    .GroupBy(item => item.Source, StringComparer.Ordinal)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                DailyMetricsStatusCounts = active
                    .GroupBy(item => item.DailyMetricsStatus, StringComparer.Ordinal)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                Logs =
                [
                    $"candidate ledger rebuilt: {active.Count}active / {startDate}-{endDate}",
                    $"active path: {_store.ActivePath}",
                    "score fields are intentionally left Pending",
                    "market cap, listed shares, and floating shares remain Pending until a data source is attached"
                ]
            };
            _store.SaveSummary(summary);
            return summary;
        }

        private static CandidateLedgerEntry BuildEntry(
            DailySeries series,
            BacktestDailyBar today,
            BacktestDailyBar? previous,
            int todayIndex,
            string runId,
            IReadOnlyDictionary<string, string> stockNameByCode)
        {
            string code = series.Code;
            string date = BacktestDataStore.NormalizeDate(today.Date);
            long tradingValue = ResolveTradingValue(today);
            long previousTradingValue = previous == null ? 0 : ResolveTradingValue(previous);
            decimal changeRate = ResolveChangeRate(today);
            decimal closeLocation = ResolveCloseLocationPercent(today);
            decimal upperTail = ResolveUpperTailPercent(today);
            decimal lowerTail = ResolveLowerTailPercent(today);
            List<BacktestDailyBar> previous20 = [.. series.Bars
                .Take(todayIndex)
                .TakeLast(20)];

            decimal? avgVolume20 = previous20.Count > 0 ? previous20.Average(bar => (decimal)bar.Volume) : null;
            decimal? avgTradingValue20 = previous20.Count > 0 ? previous20.Average(bar => (decimal)ResolveTradingValue(bar)) : null;
            decimal? avgRange20 = previous20.Count > 0 ? previous20.Average(ResolveRangePercent) : null;
            decimal? avgChange20 = previous20.Count > 0 ? previous20.Average(ResolveChangeRate) : null;
            decimal? bollingerUpper20 = previous20.Count >= 19
                ? ResolveBollingerUpper20([.. previous20, today])
                : null;

            string missing = "MarketCap;ListedShares;FloatingShares";
            return new CandidateLedgerEntry
            {
                Key = $"{code}|KRX|{date}|DataStoreRebuild",
                Code = code,
                Name = stockNameByCode.TryGetValue(code, out string? name) ? name : code,
                Market = "KRX",
                CandidateTime = $"{date}000000",
                CandidateDate = date,
                ConditionName = "DataStore recent 6D 100B+25%",
                ConditionId = "DATASTORE_RECENT6_100B_25P",
                Source = "DataStoreRebuild",
                SorMode = "SOR_READY",
                CurrentPrice = today.Close,
                ChangeRate = changeRate,
                ChangeAmount = previous != null ? today.Close - previous.Close : 0,
                OpenPrice = today.Open,
                HighPrice = today.High,
                LowPrice = today.Low,
                ExpectedClosePrice = today.Close,
                PriceUpdatedAt = runId,
                TodayOpen = today.Open,
                TodayHigh = today.High,
                TodayLow = today.Low,
                TodayClose = today.Close,
                TodayVolume = today.Volume,
                TodayTradingValue = tradingValue,
                TodayBarStatus = today.Status,
                TodayBarUpdatedAt = string.IsNullOrWhiteSpace(today.UpdatedAt) ? runId : today.UpdatedAt,
                PrevOpen = previous?.Open ?? 0,
                PrevHigh = previous?.High ?? 0,
                PrevLow = previous?.Low ?? 0,
                PrevClose = previous?.Close ?? 0,
                PrevVolume = previous?.Volume ?? 0,
                PrevTradingValue = previousTradingValue,
                PrevBarDate = previous?.Date ?? string.Empty,
                PrevBarStatus = previous?.Status ?? "Pending",
                Volume = today.Volume,
                TradingValue = tradingValue,
                VolumeSource = "DataStoreDaily",
                TradingValueSource = "DataStoreDaily",
                DataMarket = "KRX",
                PriceMarket = "KRX",
                VolumeMarket = "KRX",
                TradingValueMarket = "KRX",
                VolumeVsPrevDayRatioPercent = previous?.Volume > 0 ? today.Volume / (decimal)previous.Volume * 100m : null,
                VolumeVsPrevDayIncreasePercent = previous?.Volume > 0 ? (today.Volume - previous.Volume) / (decimal)previous.Volume * 100m : null,
                TradingValueVsPrevDayRatioPercent = previousTradingValue > 0 ? tradingValue / (decimal)previousTradingValue * 100m : null,
                TradingValueVsPrevDayIncreasePercent = previousTradingValue > 0 ? (tradingValue - previousTradingValue) / (decimal)previousTradingValue * 100m : null,
                AvgVolume20D = avgVolume20,
                AvgTradingValue20D = avgTradingValue20,
                AvgRange20D = avgRange20,
                AvgChangeRate20D = avgChange20,
                DailyBarsLoadedCount = previous20.Count + 1,
                DailyMetricsStatus = previous20.Count >= 20 ? "Calculated" : "Loaded",
                DailyMetricsUpdatedAt = runId,
                VolumeToAvg20RatioPercent = avgVolume20 > 0 ? today.Volume / avgVolume20 * 100m : null,
                VolumeIncreaseVsAvg20Percent = avgVolume20 > 0 ? (today.Volume - avgVolume20) / avgVolume20 * 100m : null,
                TradingValueToAvg20RatioPercent = avgTradingValue20 > 0 ? tradingValue / avgTradingValue20 * 100m : null,
                TradingValueIncreaseVsAvg20Percent = avgTradingValue20 > 0 ? (tradingValue - avgTradingValue20) / avgTradingValue20 * 100m : null,
                IsBullishCandle = today.Close > today.Open,
                CandleRangePercent = ResolveRangePercent(today),
                BodyPercent = today.Open > 0 ? Math.Abs(today.Close - today.Open) / (decimal)today.Open * 100m : null,
                UpperTailPercent = upperTail,
                LowerTailPercent = lowerTail,
                CloseLocationPercent = closeLocation,
                IsLimitUpLike = changeRate >= 29.5m,
                IsBollingerUpperBreak = bollingerUpper20.HasValue ? today.Close > bollingerUpper20.Value : null,
                BollingerUpper20 = bollingerUpper20,
                PrevHighBreak = previous?.High > 0 ? today.High > previous.High : null,
                DistanceFromPrevHighPercent = previous?.High > 0 ? (today.Close - previous.High) / (decimal)previous.High * 100m : null,
                TodayCloseAbovePrevHigh = previous?.High > 0 ? today.Close > previous.High : null,
                TodayLowAbovePrevHigh = previous?.High > 0 ? today.Low > previous.High : null,
                PrevCloseGapPercent = previous?.Close > 0 ? (today.Open - previous.Close) / (decimal)previous.Close * 100m : null,
                FundamentalStatus = "Pending",
                MarketLogicStatus = "Pending",
                ScoreStatus = "Pending",
                MissingFieldMemo = missing,
                Status = "Active",
                SavedAt = runId,
                UpdatedAt = runId
            };
        }

        private static List<DailySeries> LoadKrxDailySeries(BacktestDataStore dataStore)
        {
            string dailyDirectory = Path.Combine(dataStore.RootPath, "daily");
            if (!Directory.Exists(dailyDirectory))
                return [];

            List<DailySeries> result = [];
            foreach (string path in Directory.EnumerateFiles(dailyDirectory, "*_KRX_daily.json", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                string code = BacktestDataStore.NormalizeCode(fileName.Split('_').FirstOrDefault() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                List<BacktestDailyBar> bars = [.. dataStore.LoadDailyBars(code, "KRX")
                    .Where(bar => bar != null && !string.IsNullOrWhiteSpace(bar.Date))
                    .OrderBy(bar => BacktestDataStore.NormalizeDate(bar.Date))];
                if (bars.Count > 0)
                    result.Add(new DailySeries(code, bars));
            }

            return result;
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

        private static decimal ResolveRangePercent(BacktestDailyBar bar)
        {
            if (bar.Open <= 0)
                return 0m;

            return (bar.High - bar.Low) / (decimal)bar.Open * 100m;
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

        private static decimal ResolveLowerTailPercent(BacktestDailyBar bar)
        {
            long range = bar.High - bar.Low;
            if (range <= 0)
                return 0m;

            long bodyLow = Math.Min(bar.Open, bar.Close);
            return (bodyLow - bar.Low) / (decimal)range * 100m;
        }

        private static decimal? ResolveBollingerUpper20(IReadOnlyList<BacktestDailyBar> bars)
        {
            const int period = 20;
            if (bars.Count < period)
                return null;

            List<decimal> closes = [.. bars.TakeLast(period).Select(bar => (decimal)bar.Close)];
            decimal average = closes.Average();
            decimal variance = closes.Sum(close => (close - average) * (close - average)) / period;
            decimal stdDev = (decimal)Math.Sqrt((double)variance);
            return average + stdDev * 2m;
        }

        private sealed record DailySeries(string Code, List<BacktestDailyBar> Bars);
    }
}
