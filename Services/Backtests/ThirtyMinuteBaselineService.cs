using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class ThirtyMinuteBaselineService
    {
        private const int DefaultLookbackBars = 600;
        private const int PivotWingBars = 3;
        private const decimal DefaultClusterToleranceRate = 0.008m;
        private const decimal TouchToleranceRate = 0.006m;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly BacktestDataStore _dataStore;

        public ThirtyMinuteBaselineService(BacktestDataStore? dataStore = null)
        {
            _dataStore = dataStore ?? new BacktestDataStore();
        }

        public ThirtyMinuteBaselineSnapshot Analyze(
            string code,
            string market,
            string? anchorTime = null,
            int lookbackBars = DefaultLookbackBars)
        {
            List<BacktestMinuteBar> bars = LoadThirtyMinuteBarsForMarket(code, market);

            return Analyze(code, market, bars, anchorTime, lookbackBars);
        }

        public ThirtyMinuteBaselineSnapshot Analyze(
            string code,
            string market,
            IReadOnlyList<BacktestMinuteBar> sourceBars,
            string? anchorTime = null,
            int lookbackBars = DefaultLookbackBars)
        {
            List<BacktestMinuteBar> ordered = [.. (sourceBars ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DateTime))
                .OrderBy(item => item.DateTime, StringComparer.Ordinal)];

            if (!string.IsNullOrWhiteSpace(anchorTime))
                ordered = [.. ordered.Where(item => string.CompareOrdinal(item.DateTime, anchorTime) <= 0)];

            List<BacktestMinuteBar> window = [.. ordered.TakeLast(Math.Max(60, lookbackBars))];
            if (window.Count == 0)
            {
                return new ThirtyMinuteBaselineSnapshot(
                    BacktestDataStore.NormalizeCode(code),
                    BacktestDataStore.NormalizeMarket(market),
                    string.Empty,
                    0,
                    0,
                    0,
                    [],
                    null,
                    null,
                    0,
                    "NO_DATA");
            }

            BacktestMinuteBar anchor = window[^1];
            long anchorPrice = Math.Max(0, anchor.Close);
            List<PivotPoint> pivots = FindPivotPoints(window);
            List<ThirtyMinuteBaselineLevel> levels = BuildLevels(window, pivots, anchorPrice);
            ThirtyMinuteBaselineLevel? nearestSupport = levels
                .Where(item => item.Price <= anchorPrice)
                .OrderBy(item => Math.Abs(item.DistanceFromAnchorPct))
                .ThenByDescending(item => item.Score)
                .FirstOrDefault();
            long resistanceFloor = anchorPrice > 0
                ? (long)Math.Ceiling(anchorPrice * (1m + TouchToleranceRate))
                : anchorPrice;
            ThirtyMinuteBaselineLevel? nearestResistance = levels
                .Where(item => item.Price >= resistanceFloor)
                .OrderBy(item => Math.Abs(item.DistanceFromAnchorPct))
                .ThenByDescending(item => item.Score)
                .FirstOrDefault();

            decimal breakoutRoomPct = nearestResistance == null || anchorPrice <= 0
                ? 99m
                : (nearestResistance.Price - anchorPrice) / (decimal)anchorPrice * 100m;

            string locationTag = ResolveLocationTag(anchorPrice, nearestSupport, nearestResistance, breakoutRoomPct);

            return new ThirtyMinuteBaselineSnapshot(
                BacktestDataStore.NormalizeCode(code),
                BacktestDataStore.NormalizeMarket(market),
                anchor.DateTime,
                anchorPrice,
                window.Count,
                pivots.Count,
                levels,
                nearestSupport,
                nearestResistance,
                breakoutRoomPct,
                locationTag);
        }

        public string SaveFullScan(int lookbackBars = DefaultLookbackBars)
        {
            string runId = $"SORON_30m_baseline_scan_{DateTime.Now:yyyyMMddHHmmss}";
            string outputDirectory = Path.Combine(ResolveRunRootPath(), runId);
            Directory.CreateDirectory(outputDirectory);

            List<ThirtyMinuteBaselineSnapshot> snapshots = [];
            foreach ((string code, string market, string path) in EnumerateThirtyMinuteFiles())
            {
                List<BacktestMinuteBar> bars = LoadBars(path);
                if (bars.Count == 0)
                    continue;

                snapshots.Add(Analyze(code, market, bars, null, lookbackBars));
            }

            string csvPath = Path.Combine(outputDirectory, "thirty_minute_baselines.csv");
            File.WriteAllText(csvPath, BuildSnapshotCsv(snapshots), Encoding.UTF8);
            string summaryPath = Path.Combine(outputDirectory, "summary.json");
            var summary = new
            {
                RunId = runId,
                LookbackBars = lookbackBars,
                SnapshotCount = snapshots.Count,
                LevelCount = snapshots.Sum(item => item.Levels.Count),
                TightResistanceCount = snapshots.Count(item => item.BreakoutRoomPct >= 0 && item.BreakoutRoomPct <= 2),
                Output = csvPath
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            return outputDirectory;
        }

        public string SaveCandidateChartSmoke(
            int lookbackBars = DefaultLookbackBars,
            int chartBars = 240,
            int maxCharts = 12)
        {
            string runId = $"SORON_30m_baseline_chart_smoke_{DateTime.Now:yyyyMMddHHmmss}";
            string outputDirectory = Path.Combine(ResolveRunRootPath(), runId);
            string chartDirectory = Path.Combine(outputDirectory, "charts");
            Directory.CreateDirectory(chartDirectory);

            List<CandidateChartRow> rows = [];
            HashSet<string> renderedKeys = new(StringComparer.Ordinal);
            List<BacktestBaseCandle> candidates = [.. _dataStore.LoadBaseCandles()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .OrderByDescending(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate))
                .ThenByDescending(item => item.BaseTradingValue)
                .GroupBy(item => $"{BacktestDataStore.NormalizeCode(item.Code)}|{BacktestDataStore.NormalizeMarket(item.Market)}", StringComparer.Ordinal)
                .Select(group => group.First())];

            foreach (BacktestBaseCandle candidate in candidates)
            {
                if (rows.Count >= Math.Max(1, maxCharts))
                    break;

                string code = BacktestDataStore.NormalizeCode(candidate.Code);
                string? market = ResolveAvailableThirtyMinuteMarket(code, candidate.Market, candidate.BaseCandleMarket);
                if (string.IsNullOrWhiteSpace(market))
                    continue;

                string renderKey = $"{code}|{market}";
                if (!renderedKeys.Add(renderKey))
                    continue;

                List<BacktestMinuteBar> bars = [.. _dataStore.LoadMinuteBars(code, market, 30)
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DateTime))
                    .OrderBy(item => item.DateTime, StringComparer.Ordinal)];
                if (bars.Count < 80)
                    continue;

                ThirtyMinuteBaselineSnapshot snapshot = Analyze(code, market, bars, null, lookbackBars);
                if (snapshot.Levels.Count == 0)
                    continue;

                string safeName = $"{code}_{market}_{BacktestDataStore.NormalizeDate(candidate.BaseCandleDate)}_30m_baseline.png";
                string chartPath = Path.Combine(chartDirectory, safeName);
                RenderBaselineChart(chartPath, candidate, bars, snapshot, chartBars);
                rows.Add(new CandidateChartRow(
                    code,
                    candidate.Name,
                    BacktestDataStore.NormalizeMarket(candidate.Market),
                    market,
                    BacktestDataStore.NormalizeDate(candidate.BaseCandleDate),
                    snapshot.AnchorTime,
                    snapshot.AnchorClose,
                    snapshot.NearestSupport?.Price ?? 0,
                    snapshot.NearestResistance?.Price ?? 0,
                    snapshot.BreakoutRoomPct,
                    snapshot.LocationTag,
                    chartPath));
            }

            string csvPath = Path.Combine(outputDirectory, "chart_smoke_summary.csv");
            File.WriteAllText(csvPath, BuildCandidateChartCsv(rows), Encoding.UTF8);
            string summaryPath = Path.Combine(outputDirectory, "summary.json");
            var summary = new
            {
                RunId = runId,
                LookbackBars = lookbackBars,
                ChartBars = chartBars,
                CandidateCount = candidates.Count,
                ChartCount = rows.Count,
                Output = outputDirectory,
                Csv = csvPath
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            return outputDirectory;
        }

        public string SaveCodeChartSmoke(
            IEnumerable<string> codes,
            int lookbackBars = DefaultLookbackBars,
            int chartBars = 260)
        {
            string runId = $"SORON_30m_baseline_chart_codes_{DateTime.Now:yyyyMMddHHmmss}";
            string outputDirectory = Path.Combine(ResolveRunRootPath(), runId);
            string chartDirectory = Path.Combine(outputDirectory, "charts");
            Directory.CreateDirectory(chartDirectory);

            Dictionary<string, BacktestBaseCandle> baseByCodeMarket = _dataStore.LoadBaseCandles()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{BacktestDataStore.NormalizeCode(item.Code)}|{BacktestDataStore.NormalizeMarket(item.Market)}", StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate))
                        .ThenByDescending(item => item.BaseTradingValue)
                        .First(),
                    StringComparer.Ordinal);

            List<CandidateChartRow> rows = [];
            foreach (string code in (codes ?? []).Select(BacktestDataStore.NormalizeCode).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal))
            {
                foreach (string market in EnumerateAvailableThirtyMinuteMarkets(code))
                {
                    List<BacktestMinuteBar> bars = [.. _dataStore.LoadMinuteBars(code, market, 30)
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DateTime))
                        .OrderBy(item => item.DateTime, StringComparer.Ordinal)];
                    if (bars.Count < 80)
                        continue;

                    BacktestBaseCandle candidate = baseByCodeMarket.TryGetValue($"{code}|{market}", out BacktestBaseCandle? found)
                        ? found
                        : new BacktestBaseCandle
                        {
                            Code = code,
                            Name = code,
                            Market = market,
                            BaseCandleMarket = market,
                            BaseCandleDate = bars[^1].DateTime.Length >= 8 ? bars[^1].DateTime[..8] : string.Empty,
                            BaseClose = bars[^1].Close
                        };

                    ThirtyMinuteBaselineSnapshot snapshot = Analyze(code, market, bars, null, lookbackBars);
                    if (snapshot.Levels.Count == 0)
                        continue;

                    string safeName = $"{code}_{market}_{BacktestDataStore.NormalizeDate(candidate.BaseCandleDate)}_30m_baseline.png";
                    string chartPath = Path.Combine(chartDirectory, safeName);
                    RenderBaselineChart(chartPath, candidate, bars, snapshot, chartBars);
                    rows.Add(new CandidateChartRow(
                        code,
                        candidate.Name,
                        BacktestDataStore.NormalizeMarket(candidate.Market),
                        market,
                        BacktestDataStore.NormalizeDate(candidate.BaseCandleDate),
                        snapshot.AnchorTime,
                        snapshot.AnchorClose,
                        snapshot.NearestSupport?.Price ?? 0,
                        snapshot.NearestResistance?.Price ?? 0,
                        snapshot.BreakoutRoomPct,
                        snapshot.LocationTag,
                        chartPath));
                }
            }

            string csvPath = Path.Combine(outputDirectory, "chart_code_summary.csv");
            File.WriteAllText(csvPath, BuildCandidateChartCsv(rows), Encoding.UTF8);
            string summaryPath = Path.Combine(outputDirectory, "summary.json");
            var summary = new
            {
                RunId = runId,
                LookbackBars = lookbackBars,
                ChartBars = chartBars,
                RequestedCodes = (codes ?? []).Select(BacktestDataStore.NormalizeCode).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray(),
                ChartCount = rows.Count,
                Output = outputDirectory,
                Csv = csvPath
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            return outputDirectory;
        }

        public ThirtyMinuteBaselineSignalChartResult? SaveSignalChart(
            string outputDirectory,
            string code,
            string market,
            string anchorTime,
            int lookbackBars = DefaultLookbackBars,
            int chartBars = 260)
        {
            string normalizedCode = BacktestDataStore.NormalizeCode(code);
            string normalizedMarket = BacktestDataStore.NormalizeMarket(market);
            string normalizedAnchorTime = new([.. (anchorTime ?? string.Empty).Where(char.IsDigit)]);
            if (string.IsNullOrWhiteSpace(outputDirectory) ||
                string.IsNullOrWhiteSpace(normalizedCode) ||
                string.IsNullOrWhiteSpace(normalizedMarket) ||
                normalizedAnchorTime.Length < 12)
            {
                return null;
            }

            List<BacktestMinuteBar> allBars = LoadThirtyMinuteBarsForMarket(normalizedCode, normalizedMarket);
            if (allBars.Count == 0)
                return null;
            int anchorIndex = FindLastTimeIndexAtOrBefore(allBars, normalizedAnchorTime);
            if (anchorIndex < 0)
                return null;

            List<BacktestMinuteBar> knownBars = [.. allBars.Take(anchorIndex + 1)];
            if (knownBars.Count < 80)
                return null;

            ThirtyMinuteBaselineSnapshot snapshot = Analyze(normalizedCode, normalizedMarket, knownBars, normalizedAnchorTime, lookbackBars);
            if (snapshot.Levels.Count == 0)
                return null;

            Directory.CreateDirectory(outputDirectory);
            string safeAnchor = normalizedAnchorTime.Length >= 14 ? normalizedAnchorTime[..14] : normalizedAnchorTime;
            string chartPath = Path.Combine(outputDirectory, $"{normalizedCode}_{normalizedMarket}_{safeAnchor}_30m_structure.png");
            var candidate = new BacktestBaseCandle
            {
                Code = normalizedCode,
                Name = normalizedCode,
                Market = normalizedMarket,
                BaseCandleMarket = normalizedMarket,
                BaseCandleDate = safeAnchor.Length >= 8 ? safeAnchor[..8] : string.Empty,
                BaseClose = snapshot.AnchorClose
            };

            RenderBaselineChart(chartPath, candidate, knownBars, snapshot, chartBars, normalizedAnchorTime, "ENTRY");
            return new ThirtyMinuteBaselineSignalChartResult(
                normalizedCode,
                normalizedMarket,
                normalizedAnchorTime,
                chartPath,
                snapshot.AnchorTime,
                snapshot.AnchorClose,
                snapshot.NearestSupport?.Price ?? 0,
                snapshot.NearestResistance?.Price ?? 0,
                snapshot.BreakoutRoomPct,
                snapshot.LocationTag);
        }

        private static List<PivotPoint> FindPivotPoints(IReadOnlyList<BacktestMinuteBar> bars)
        {
            List<PivotPoint> pivots = [];
            if (bars.Count < PivotWingBars * 2 + 1)
                return pivots;

            for (int i = PivotWingBars; i < bars.Count - PivotWingBars; i++)
            {
                BacktestMinuteBar current = bars[i];
                bool pivotHigh = true;
                bool pivotLow = true;
                for (int j = i - PivotWingBars; j <= i + PivotWingBars; j++)
                {
                    if (j == i)
                        continue;

                    if (bars[j].High > current.High)
                        pivotHigh = false;
                    if (bars[j].Low < current.Low)
                        pivotLow = false;
                }

                decimal recencyWeight = 1m + i / (decimal)Math.Max(1, bars.Count - 1);
                decimal valueWeight = current.TradingValue > 0
                    ? 1m + Math.Min(4m, current.TradingValue / 10_000_000_000m)
                    : 1m;
                decimal weight = recencyWeight * valueWeight;

                if (pivotHigh && current.High > 0)
                    pivots.Add(new PivotPoint(current.High, current.DateTime, "R", weight, current.TradingValue));
                if (pivotLow && current.Low > 0)
                    pivots.Add(new PivotPoint(current.Low, current.DateTime, "S", weight, current.TradingValue));
            }

            return pivots;
        }

        private static List<ThirtyMinuteBaselineLevel> BuildLevels(
            IReadOnlyList<BacktestMinuteBar> bars,
            IReadOnlyList<PivotPoint> pivots,
            long anchorPrice)
        {
            if (pivots.Count == 0)
                return [];

            List<PivotCluster> clusters = [];
            foreach (PivotPoint pivot in pivots.OrderBy(item => item.Price))
            {
                PivotCluster? cluster = clusters.FirstOrDefault(item => IsNear(item.Center, pivot.Price, DefaultClusterToleranceRate));
                if (cluster == null)
                {
                    cluster = new PivotCluster();
                    clusters.Add(cluster);
                }

                cluster.Add(pivot);
            }

            List<ThirtyMinuteBaselineLevel> levels = [];
            foreach (PivotCluster cluster in clusters)
            {
                if (cluster.Points.Count < 2)
                    continue;

                long price = cluster.Center;
                int supportTouches = 0;
                int resistanceTouches = 0;
                long touchTradingValue = 0;
                string firstTime = cluster.Points.Select(item => item.Time).Min(StringComparer.Ordinal) ?? string.Empty;
                string lastTime = cluster.Points.Select(item => item.Time).Max(StringComparer.Ordinal) ?? string.Empty;

                foreach (BacktestMinuteBar bar in bars)
                {
                    if (!BarTouchesPrice(bar, price, TouchToleranceRate))
                        continue;

                    touchTradingValue += Math.Max(0, bar.TradingValue);
                    if (bar.Close >= price)
                        supportTouches++;
                    if (bar.Close <= price)
                        resistanceTouches++;
                    if (string.CompareOrdinal(bar.DateTime, firstTime) < 0)
                        firstTime = bar.DateTime;
                    if (string.CompareOrdinal(bar.DateTime, lastTime) > 0)
                        lastTime = bar.DateTime;
                }

                int touchCount = supportTouches + resistanceTouches;
                if (touchCount < 3)
                    continue;

                string role = supportTouches > resistanceTouches * 1.4m
                    ? "SUPPORT"
                    : resistanceTouches > supportTouches * 1.4m
                        ? "RESISTANCE"
                        : "MIXED";

                decimal distance = anchorPrice > 0
                    ? (price - anchorPrice) / (decimal)anchorPrice * 100m
                    : 0m;
                decimal score =
                    cluster.Points.Sum(item => item.Weight) +
                    touchCount * 1.2m +
                    Math.Min(10m, touchTradingValue / 20_000_000_000m);

                levels.Add(new ThirtyMinuteBaselineLevel(
                    price,
                    Math.Round(score, 2),
                    cluster.Points.Count,
                    supportTouches,
                    resistanceTouches,
                    role,
                    firstTime,
                    lastTime,
                    touchTradingValue,
                    Math.Round(distance, 2)));
            }

            return [.. levels
                .OrderByDescending(item => item.Score)
                .ThenBy(item => Math.Abs(item.DistanceFromAnchorPct))
                .Take(12)];
        }

        private static string ResolveLocationTag(
            long anchorPrice,
            ThirtyMinuteBaselineLevel? nearestSupport,
            ThirtyMinuteBaselineLevel? nearestResistance,
            decimal breakoutRoomPct)
        {
            if (anchorPrice <= 0)
                return "NO_PRICE";

            bool nearSupport = nearestSupport != null && Math.Abs(nearestSupport.DistanceFromAnchorPct) <= 1.5m;
            bool nearResistance = nearestResistance != null && nearestResistance.DistanceFromAnchorPct >= 0 && nearestResistance.DistanceFromAnchorPct <= 1.5m;

            if (nearResistance && !nearSupport)
                return "NEAR_30M_RESISTANCE";
            if (nearSupport && breakoutRoomPct >= 3m)
                return "SUPPORT_WITH_ROOM";
            if (nearSupport)
                return "SUPPORT_BUT_ROOM_THIN";
            if (breakoutRoomPct < 2m)
                return "ROOM_THIN";
            return "OPEN_MIDDLE";
        }

        private IEnumerable<(string Code, string Market, string Path)> EnumerateThirtyMinuteFiles()
        {
            string directory = Path.Combine(_dataStore.RootPath, "minute", "30m");
            if (!Directory.Exists(directory))
                yield break;

            foreach (string path in Directory.EnumerateFiles(directory, "*_30m.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                yield return (BacktestDataStore.NormalizeCode(parts[0]), BacktestDataStore.NormalizeMarket(parts[1]), path);
            }
        }

        private static List<BacktestMinuteBar> LoadBars(string path)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<BacktestMinuteBar>>(json, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static string BuildSnapshotCsv(IEnumerable<ThirtyMinuteBaselineSnapshot> snapshots)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Code,Market,AnchorTime,AnchorClose,WindowBars,PivotCount,LevelCount,NearestSupport,NearestSupportDistancePct,NearestResistance,NearestResistanceDistancePct,BreakoutRoomPct,LocationTag,TopLevels");
            foreach (ThirtyMinuteBaselineSnapshot item in snapshots.OrderBy(item => item.Code).ThenBy(item => item.Market))
            {
                string topLevels = string.Join(" | ", item.Levels
                    .Take(5)
                    .Select(level => $"{level.Role}:{level.Price}:{level.Score}:S{level.SupportTouches}/R{level.ResistanceTouches}:{level.DistanceFromAnchorPct:0.##}%"));
                sb.AppendLine(string.Join(',',
                    Escape(item.Code),
                    Escape(item.Market),
                    Escape(item.AnchorTime),
                    item.AnchorClose.ToString(CultureInfo.InvariantCulture),
                    item.WindowBars.ToString(CultureInfo.InvariantCulture),
                    item.PivotCount.ToString(CultureInfo.InvariantCulture),
                    item.Levels.Count.ToString(CultureInfo.InvariantCulture),
                    (item.NearestSupport?.Price ?? 0).ToString(CultureInfo.InvariantCulture),
                    (item.NearestSupport?.DistanceFromAnchorPct ?? 0).ToString(CultureInfo.InvariantCulture),
                    (item.NearestResistance?.Price ?? 0).ToString(CultureInfo.InvariantCulture),
                    (item.NearestResistance?.DistanceFromAnchorPct ?? 0).ToString(CultureInfo.InvariantCulture),
                    item.BreakoutRoomPct.ToString(CultureInfo.InvariantCulture),
                    Escape(item.LocationTag),
                    Escape(topLevels)));
            }

            return sb.ToString();
        }

        private string? ResolveAvailableThirtyMinuteMarket(string code, params string[] markets)
        {
            foreach (string market in markets
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(BacktestDataStore.NormalizeMarket)
                .Distinct(StringComparer.Ordinal))
            {
                string path = _dataStore.BuildMinutePath(code, market, 30);
                if (File.Exists(path))
                    return market;
            }

            return null;
        }

        private IEnumerable<string> EnumerateAvailableThirtyMinuteMarkets(string code)
        {
            string directory = Path.Combine(_dataStore.RootPath, "minute", "30m");
            if (!Directory.Exists(directory))
                yield break;

            foreach (string path in Directory.EnumerateFiles(directory, $"{BacktestDataStore.NormalizeCode(code)}_*_30m.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                yield return BacktestDataStore.NormalizeMarket(parts[1]);
            }
        }

        private List<BacktestMinuteBar> LoadThirtyMinuteBarsForMarket(string code, string market)
        {
            string normalizedCode = BacktestDataStore.NormalizeCode(code);
            string normalizedMarket = BacktestDataStore.NormalizeMarket(market);
            List<BacktestMinuteBar> exact = [.. _dataStore.LoadMinuteBars(normalizedCode, normalizedMarket, 30)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DateTime))
                .OrderBy(item => item.DateTime, StringComparer.Ordinal)];
            if (exact.Count > 0 || !string.Equals(normalizedMarket, "AL", StringComparison.Ordinal))
                return exact;

            List<BacktestMinuteBar> krx = [.. _dataStore.LoadMinuteBars(normalizedCode, "KRX", 30)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DateTime))];
            List<BacktestMinuteBar> nxt = [.. _dataStore.LoadMinuteBars(normalizedCode, "NXT", 30)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DateTime))];
            if (krx.Count == 0 && nxt.Count == 0)
                return [];

            return [.. krx.Concat(nxt)
                .GroupBy(item => item.DateTime, StringComparer.Ordinal)
                .Select(group => MergeThirtyMinuteBars(normalizedCode, group))
                .OrderBy(item => item.DateTime, StringComparer.Ordinal)];
        }

        private static BacktestMinuteBar MergeThirtyMinuteBars(string code, IGrouping<string, BacktestMinuteBar> group)
        {
            List<BacktestMinuteBar> bars = [.. group
                .OrderBy(item => item.Market == "KRX" ? 0 : 1)
                .ThenByDescending(item => item.TradingValue)
                .ThenByDescending(item => item.Volume)];
            BacktestMinuteBar openSource = bars.First();
            BacktestMinuteBar closeSource = bars
                .OrderByDescending(item => item.TradingValue)
                .ThenByDescending(item => item.Volume)
                .First();

            return new BacktestMinuteBar
            {
                Code = code,
                Market = "AL",
                Minute = 30,
                DateTime = group.Key,
                Open = openSource.Open,
                High = bars.Max(item => item.High),
                Low = bars.Min(item => item.Low),
                Close = closeSource.Close,
                Volume = bars.Sum(item => Math.Max(0, item.Volume)),
                TradingValue = bars.Sum(item => Math.Max(0, item.TradingValue)),
                Status = "MergedForReview",
                UpdatedAt = string.Empty
            };
        }

        private static string BuildCandidateChartCsv(IEnumerable<CandidateChartRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Code,Name,CandidateMarket,ChartMarket,BaseCandleDate,AnchorTime,AnchorClose,NearestSupport,NearestResistance,BreakoutRoomPct,LocationTag,ChartPath");
            foreach (CandidateChartRow row in rows)
            {
                sb.AppendLine(string.Join(',',
                    Escape(row.Code),
                    Escape(row.Name),
                    Escape(row.CandidateMarket),
                    Escape(row.ChartMarket),
                    Escape(row.BaseCandleDate),
                    Escape(row.AnchorTime),
                    row.AnchorClose.ToString(CultureInfo.InvariantCulture),
                    row.NearestSupport.ToString(CultureInfo.InvariantCulture),
                    row.NearestResistance.ToString(CultureInfo.InvariantCulture),
                    row.BreakoutRoomPct.ToString(CultureInfo.InvariantCulture),
                    Escape(row.LocationTag),
                    Escape(row.ChartPath)));
            }

            return sb.ToString();
        }

        private static void RenderBaselineChart(
            string path,
            BacktestBaseCandle candidate,
            IReadOnlyList<BacktestMinuteBar> sourceBars,
            ThirtyMinuteBaselineSnapshot snapshot,
            int chartBars,
            string? markerTime = null,
            string markerLabel = "")
        {
            const int width = 1380;
            const int height = 780;
            const int left = 72;
            const int right = 150;
            const int top = 58;
            const int priceBottom = 590;
            const int volumeTop = 620;
            const int bottom = 744;

            List<BacktestMinuteBar> bars = [.. sourceBars.TakeLast(Math.Max(80, chartBars))];
            if (bars.Count == 0)
                return;

            long priceHigh = bars.Max(item => item.High);
            long priceLow = bars.Min(item => item.Low);
            List<ThirtyMinuteBaselineLevel> levelsToDraw = [.. snapshot.Levels
                .Where(level => level.Price > 0 && level.Price >= priceLow * 0.86m && level.Price <= priceHigh * 1.14m)
                .OrderByDescending(level => ReferenceEquals(level, snapshot.NearestSupport) || ReferenceEquals(level, snapshot.NearestResistance))
                .ThenBy(level => Math.Abs(level.DistanceFromAnchorPct))
                .ThenByDescending(level => level.Score)
                .Take(6)];

            AddImportantLevel(levelsToDraw, snapshot.NearestSupport);
            AddImportantLevel(levelsToDraw, snapshot.NearestResistance);

            foreach (ThirtyMinuteBaselineLevel level in levelsToDraw)
            {
                priceHigh = Math.Max(priceHigh, level.Price);
                priceLow = Math.Min(priceLow, level.Price);
            }

            decimal padding = Math.Max(1m, (priceHigh - priceLow) * 0.08m);
            priceHigh = (long)Math.Ceiling(priceHigh + padding);
            priceLow = Math.Max(0, (long)Math.Floor(priceLow - padding));

            long maxVolume = Math.Max(1, bars.Max(item => item.Volume));
            double xStep = (width - left - right) / (double)Math.Max(1, bars.Count - 1);
            double candleWidth = Math.Max(2, Math.Min(9, xStep * 0.58));

            double PriceY(long price)
            {
                if (priceHigh <= priceLow)
                    return (top + priceBottom) / 2.0;
                return priceBottom - (price - priceLow) / (double)(priceHigh - priceLow) * (priceBottom - top);
            }

            double VolumeY(long volume) =>
                bottom - volume / (double)maxVolume * (bottom - volumeTop);

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 12, 14)), null, new Rect(0, 0, width, height));
                var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(38, 42, 48)), 1);
                for (int i = 0; i <= 6; i++)
                {
                    double y = top + i * (priceBottom - top) / 6.0;
                    dc.DrawLine(gridPen, new Point(left, y), new Point(width - right, y));
                    long labelPrice = (long)Math.Round(priceHigh - (priceHigh - priceLow) * i / 6.0);
                    DrawText(dc, labelPrice.ToString("N0", CultureInfo.InvariantCulture), width - right + 8, y - 8, 11, Color.FromRgb(185, 194, 204));
                }

                for (int i = 0; i <= 6; i++)
                {
                    double x = left + i * (width - left - right) / 6.0;
                    dc.DrawLine(gridPen, new Point(x, top), new Point(x, bottom));
                }

                dc.DrawLine(gridPen, new Point(left, volumeTop), new Point(width - right, volumeTop));
                dc.DrawLine(gridPen, new Point(left, bottom), new Point(width - right, bottom));

                string title = $"{snapshot.Code} {snapshot.Market} / 30m baseline from 600 bars / chart last {bars.Count} bars";
                string subTitle = $"base {BacktestDataStore.NormalizeDate(candidate.BaseCandleDate)} / anchor {snapshot.AnchorTime} close {snapshot.AnchorClose:N0} / {snapshot.LocationTag} / room {snapshot.BreakoutRoomPct:0.##}%";
                DrawText(dc, title, 18, 16, 16, Colors.White);
                DrawText(dc, subTitle, 18, 36, 12, Color.FromRgb(178, 190, 206));

                foreach (BaselineZone zone in BuildBaselineZones(levelsToDraw, snapshot.AnchorClose))
                {
                    double zoneTop = PriceY(zone.High);
                    double zoneBottom = PriceY(zone.Low);
                    var fill = new SolidColorBrush(Color.FromArgb(zone.IsResistance ? (byte)34 : (byte)44, zone.Color.R, zone.Color.G, zone.Color.B));
                    var border = new Pen(new SolidColorBrush(Color.FromArgb(120, zone.Color.R, zone.Color.G, zone.Color.B)), 1.1);
                    dc.DrawRectangle(fill, border, new Rect(left, Math.Min(zoneTop, zoneBottom), width - left - right, Math.Max(4, Math.Abs(zoneTop - zoneBottom))));
                    DrawText(dc, zone.Label, width - right + 8, Math.Min(zoneTop, zoneBottom) + 3, 10, zone.Color);
                }

                DrawPriceLine(dc, left, width - right, PriceY(snapshot.AnchorClose), Color.FromRgb(130, 150, 255), $"NOW {snapshot.AnchorClose:N0}", DashStyles.Dot);

                foreach (ThirtyMinuteBaselineLevel level in levelsToDraw.OrderBy(item => item.Price))
                {
                    bool isSupport = snapshot.NearestSupport != null && level.Price == snapshot.NearestSupport.Price;
                    bool isResistance = snapshot.NearestResistance != null && level.Price == snapshot.NearestResistance.Price;
                    Color color = isSupport
                        ? Color.FromRgb(80, 245, 140)
                        : isResistance
                            ? Color.FromRgb(255, 120, 80)
                            : ResolveLevelColor(level);
                    string label = $"{level.Role} {level.Price:N0} S{level.SupportTouches}/R{level.ResistanceTouches} {level.DistanceFromAnchorPct:+0.##;-0.##;0}%";
                    DrawPriceLine(dc, left, width - right, PriceY(level.Price), color, label, isSupport || isResistance ? null : DashStyles.Dash);
                }

                List<double?> ma60 = CalculateMa(bars, 60);
                List<double?> ma200 = CalculateMa(bars, 200);
                DrawMa(dc, bars, ma60, left, xStep, PriceY, Color.FromRgb(100, 220, 100));
                DrawMa(dc, bars, ma200, left, xStep, PriceY, Color.FromRgb(185, 185, 185));

                int markerIndex = !string.IsNullOrWhiteSpace(markerTime)
                    ? bars.FindIndex(item => string.Equals(item.DateTime, markerTime, StringComparison.Ordinal))
                    : -1;
                if (markerIndex >= 0)
                {
                    double markerX = left + markerIndex * xStep;
                    var markerPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 230, 80)), 2.0)
                    {
                        DashStyle = DashStyles.Dash
                    };
                    dc.DrawLine(markerPen, new Point(markerX, top), new Point(markerX, bottom));
                    DrawText(dc, string.IsNullOrWhiteSpace(markerLabel) ? "MARK" : markerLabel, markerX + 5, top + 16, 11, Color.FromRgb(255, 230, 80));
                }

                for (int i = 0; i < bars.Count; i++)
                {
                    BacktestMinuteBar bar = bars[i];
                    double x = left + i * xStep;
                    bool up = bar.Close >= bar.Open;
                    Color color = up ? Color.FromRgb(255, 76, 76) : Color.FromRgb(70, 148, 255);
                    var brush = new SolidColorBrush(color);
                    var pen = new Pen(brush, 1.05);
                    double openY = PriceY(bar.Open);
                    double closeY = PriceY(bar.Close);
                    dc.DrawLine(pen, new Point(x, PriceY(bar.High)), new Point(x, PriceY(bar.Low)));
                    dc.DrawRectangle(brush, null, new Rect(
                        x - candleWidth / 2,
                        Math.Min(openY, closeY),
                        candleWidth,
                        Math.Max(2, Math.Abs(openY - closeY))));

                    double volumeY = VolumeY(bar.Volume);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)), null, new Rect(
                        x - candleWidth / 2,
                        volumeY,
                        candleWidth,
                        bottom - volumeY));

                    if (i % Math.Max(1, bars.Count / 8) == 0 || i == bars.Count - 1)
                    {
                        string label = FormatTimeLabel(bar.DateTime);
                        DrawText(dc, label, x - 32, bottom + 8, 10, Color.FromRgb(160, 170, 182));
                    }
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private static List<BaselineZone> BuildBaselineZones(IReadOnlyList<ThirtyMinuteBaselineLevel> levels, long anchorClose)
        {
            List<BaselineZone> zones = [];
            if (levels.Count == 0)
                return zones;

            List<ThirtyMinuteBaselineLevel> ordered = [.. levels
                .Where(item => item.Price > 0)
                .OrderBy(item => item.Price)];
            List<ThirtyMinuteBaselineLevel> group = [];

            foreach (ThirtyMinuteBaselineLevel level in ordered)
            {
                if (group.Count == 0)
                {
                    group.Add(level);
                    continue;
                }

                decimal center = group.Sum(item => (decimal)item.Price) / Math.Max(1, group.Count);
                decimal gap = Math.Abs(level.Price - center) / Math.Max(1m, center);
                if (gap <= 0.026m)
                {
                    group.Add(level);
                    continue;
                }

                AddZone(zones, group, anchorClose);
                group = [level];
            }

            AddZone(zones, group, anchorClose);
            return [.. zones
                .OrderBy(item => item.Low)
                .Take(4)];
        }

        private static void AddZone(List<BaselineZone> zones, IReadOnlyList<ThirtyMinuteBaselineLevel> group, long anchorClose)
        {
            if (group.Count == 0)
                return;

            long low = group.Min(item => item.Price);
            long high = group.Max(item => item.Price);
            long center = (long)Math.Round(group.Average(item => item.Price));
            long pad = Math.Max(1, (long)Math.Round(center * 0.004m));
            if (high - low < center * 0.012m)
            {
                low = Math.Max(0, center - pad);
                high = center + pad;
            }
            else
            {
                low = Math.Max(0, low - pad);
                high += pad;
            }

            int supportTouches = group.Sum(item => item.SupportTouches);
            int resistanceTouches = group.Sum(item => item.ResistanceTouches);
            bool isResistance = anchorClose > 0 && center > anchorClose;
            Color color = isResistance
                ? Color.FromRgb(240, 150, 70)
                : Color.FromRgb(85, 230, 130);
            string label = $"{(isResistance ? "RESIST ZONE" : "WAIST ZONE")} {low:N0}-{high:N0} S{supportTouches}/R{resistanceTouches}";
            zones.Add(new BaselineZone(low, high, color, isResistance, label));
        }

        private static void AddImportantLevel(List<ThirtyMinuteBaselineLevel> levels, ThirtyMinuteBaselineLevel? level)
        {
            if (level == null || levels.Any(item => item.Price == level.Price))
                return;

            levels.Add(level);
        }

        private static Color ResolveLevelColor(ThirtyMinuteBaselineLevel level) =>
            level.Role switch
            {
                "SUPPORT" => Color.FromRgb(70, 185, 125),
                "RESISTANCE" => Color.FromRgb(230, 145, 80),
                _ => Color.FromRgb(120, 190, 220)
            };

        private static void DrawPriceLine(DrawingContext dc, int left, int right, double y, Color color, string label, DashStyle? dashStyle)
        {
            var pen = new Pen(new SolidColorBrush(color), 1.7);
            if (dashStyle != null)
                pen.DashStyle = dashStyle;
            dc.DrawLine(pen, new Point(left, y), new Point(right, y));
            DrawText(dc, label, right + 8, y - 10, 10.5, color);
        }

        private static void DrawMa(
            DrawingContext dc,
            IReadOnlyList<BacktestMinuteBar> bars,
            IReadOnlyList<double?> ma,
            int left,
            double xStep,
            Func<long, double> priceY,
            Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 1.25);
            Point? previous = null;
            for (int i = 0; i < bars.Count; i++)
            {
                if (!ma[i].HasValue)
                    continue;

                var current = new Point(left + i * xStep, priceY((long)Math.Round(ma[i]!.Value)));
                if (previous.HasValue)
                    dc.DrawLine(pen, previous.Value, current);
                previous = current;
            }
        }

        private static List<double?> CalculateMa(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new List<double?>(bars.Count);
            decimal rolling = 0m;
            Queue<long> queue = new();
            foreach (BacktestMinuteBar bar in bars)
            {
                queue.Enqueue(bar.Close);
                rolling += bar.Close;
                if (queue.Count > period)
                    rolling -= queue.Dequeue();

                result.Add(queue.Count == period ? (double)(rolling / period) : null);
            }

            return result;
        }

        private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
        {
            var formatted = new FormattedText(
                text ?? string.Empty,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                size,
                new SolidColorBrush(color),
                1.0);
            dc.DrawText(formatted, new Point(x, y));
        }

        private static string FormatTimeLabel(string dateTime)
        {
            string digits = new([.. (dateTime ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 12)
                return $"{digits[4..6]}/{digits[6..8]} {digits[8..10]}:{digits[10..12]}";
            return digits;
        }

        private static bool IsNear(long center, long price, decimal toleranceRate)
        {
            if (center <= 0 || price <= 0)
                return false;

            return Math.Abs(price - center) / (decimal)center <= toleranceRate;
        }

        private static bool BarTouchesPrice(BacktestMinuteBar bar, long price, decimal toleranceRate)
        {
            if (price <= 0)
                return false;

            decimal lowBand = price * (1m - toleranceRate);
            decimal highBand = price * (1m + toleranceRate);
            return bar.Low <= highBand && bar.High >= lowBand;
        }

        private static int FindLastTimeIndexAtOrBefore(IReadOnlyList<BacktestMinuteBar> bars, string dateTime)
        {
            for (int i = bars.Count - 1; i >= 0; i--)
            {
                if (string.CompareOrdinal(bars[i].DateTime, dateTime) <= 0)
                    return i;
            }

            return -1;
        }

        private static string Escape(string value)
        {
            string text = value ?? string.Empty;
            if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
                return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string ResolveRunRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
                return Path.Combine(Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory(), "Storage", "Backtests", "Runs");

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
                return Path.Combine(Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory, "Storage", "Backtests", "Runs");

            return Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Backtests", "Runs");
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

        private sealed record PivotPoint(long Price, string Time, string Kind, decimal Weight, long TradingValue);

        private sealed class PivotCluster
        {
            private decimal _weightedPrice;
            private decimal _weight;

            public List<PivotPoint> Points { get; } = [];

            public long Center => _weight <= 0 ? 0 : (long)Math.Round(_weightedPrice / _weight, MidpointRounding.AwayFromZero);

            public void Add(PivotPoint point)
            {
                Points.Add(point);
                decimal weight = Math.Max(0.01m, point.Weight);
                _weightedPrice += point.Price * weight;
                _weight += weight;
            }
        }

        private sealed record CandidateChartRow(
            string Code,
            string Name,
            string CandidateMarket,
            string ChartMarket,
            string BaseCandleDate,
            string AnchorTime,
            long AnchorClose,
            long NearestSupport,
            long NearestResistance,
            decimal BreakoutRoomPct,
            string LocationTag,
            string ChartPath);

        private sealed record BaselineZone(long Low, long High, Color Color, bool IsResistance, string Label);
    }

    public sealed record ThirtyMinuteBaselineSnapshot(
        string Code,
        string Market,
        string AnchorTime,
        long AnchorClose,
        int WindowBars,
        int PivotCount,
        IReadOnlyList<ThirtyMinuteBaselineLevel> Levels,
        ThirtyMinuteBaselineLevel? NearestSupport,
        ThirtyMinuteBaselineLevel? NearestResistance,
        decimal BreakoutRoomPct,
        string LocationTag);

    public sealed record ThirtyMinuteBaselineSignalChartResult(
        string Code,
        string Market,
        string EntryTime,
        string ChartPath,
        string AnchorTime,
        long AnchorClose,
        long NearestSupport,
        long NearestResistance,
        decimal BreakoutRoomPct,
        string LocationTag);

    public sealed record ThirtyMinuteBaselineLevel(
        long Price,
        decimal Score,
        int PivotCount,
        int SupportTouches,
        int ResistanceTouches,
        string Role,
        string FirstTime,
        string LastTime,
        long TouchTradingValue,
        decimal DistanceFromAnchorPct);
}
