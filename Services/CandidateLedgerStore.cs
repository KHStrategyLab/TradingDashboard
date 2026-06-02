using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class CandidateLedgerStore
    {
        private const string RelativeRoot = "Storage/CandidateLedger";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly string _rootPath;

        public CandidateLedgerStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public string ActivePath => Path.Combine(_rootPath, "active_candidates.json");

        public IReadOnlyList<CandidateLedgerEntry> LoadActive()
        {
            if (!File.Exists(ActivePath))
                return [];

            try
            {
                string json = File.ReadAllText(ActivePath);
                return JsonSerializer.Deserialize<List<CandidateLedgerEntry>>(json, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public void SaveActive(IEnumerable<CandidateLedgerEntry> entries)
        {
            List<CandidateLedgerEntry> rows = [.. (entries ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.CandidateDate))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                .OrderByDescending(item => item.CandidateDate)
                .ThenByDescending(item => item.TradingValue)
                .ThenBy(item => item.Code)];

            SaveJson(ActivePath, rows);
            SaveActiveCsv(Path.Combine(_rootPath, "active_candidates.csv"), rows);
        }

        public void SaveArchiveSnapshot(IEnumerable<CandidateLedgerEntry> entries, string runId)
        {
            string safeRunId = string.IsNullOrWhiteSpace(runId) ? DateTime.Now.ToString("yyyyMMddHHmmss") : runId;
            SaveJson(Path.Combine(_rootPath, "archive", $"candidates_{safeRunId}.json"), entries?.ToList() ?? []);
        }

        public void SaveSummary(CandidateLedgerRebuildSummary summary)
        {
            SaveJson(Path.Combine(_rootPath, "last_rebuild_summary.json"), summary);
            SaveJson(Path.Combine(_rootPath, "metadata", $"rebuild_summary_{summary.RunId}.json"), summary);
        }

        public void SaveEnrichSummary(CandidateLedgerEnrichSummary summary)
        {
            SaveJson(Path.Combine(_rootPath, "last_enrich_summary.json"), summary);
            SaveJson(Path.Combine(_rootPath, "metadata", $"enrich_summary_{summary.RunId}.json"), summary);
        }

        private static void SaveJson<T>(string path, T value)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }

        private static void SaveActiveCsv(string path, IReadOnlyList<CandidateLedgerEntry> rows)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var builder = new StringBuilder();
            builder.AppendLine("Code,Name,Market,CandidateDate,CandidateTime,Source,ConditionName,CurrentPrice,ChangeRate,DailyRsi14,Volume,TradingValue,MarketCap,MarketCapClass,ListedShares,FloatingShares,TodayOpen,TodayHigh,TodayLow,TodayClose,TodayVolume,TodayTradingValue,PrevOpen,PrevHigh,PrevLow,PrevClose,PrevVolume,PrevTradingValue,AvgTradingValue20D,ValueToMarketCapPercent,TradingValueToMarketCapPercent,TurnoverRateByListedShares,TurnoverRateByFloatingShares,CloseLocationPercent,UpperTailPercent,IsBollingerUpperBreak,PrevHighBreak,FundamentalStatus,DailyMetricsStatus,MarketLogicStatus,ScoreStatus,MissingFieldMemo");
            foreach (CandidateLedgerEntry row in rows)
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Escape(row.Code),
                    Escape(row.Name),
                    Escape(row.Market),
                    Escape(row.CandidateDate),
                    Escape(row.CandidateTime),
                    Escape(row.Source),
                    Escape(row.ConditionName),
                    row.CurrentPrice.ToString(CultureInfo.InvariantCulture),
                    row.ChangeRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.DailyRsi14?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.Volume.ToString(CultureInfo.InvariantCulture),
                    row.TradingValue.ToString(CultureInfo.InvariantCulture),
                    row.MarketCap?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    Escape(row.MarketCapClass),
                    row.ListedShares?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.FloatingShares?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.TodayOpen.ToString(CultureInfo.InvariantCulture),
                    row.TodayHigh.ToString(CultureInfo.InvariantCulture),
                    row.TodayLow.ToString(CultureInfo.InvariantCulture),
                    row.TodayClose.ToString(CultureInfo.InvariantCulture),
                    row.TodayVolume.ToString(CultureInfo.InvariantCulture),
                    row.TodayTradingValue.ToString(CultureInfo.InvariantCulture),
                    row.PrevOpen.ToString(CultureInfo.InvariantCulture),
                    row.PrevHigh.ToString(CultureInfo.InvariantCulture),
                    row.PrevLow.ToString(CultureInfo.InvariantCulture),
                    row.PrevClose.ToString(CultureInfo.InvariantCulture),
                    row.PrevVolume.ToString(CultureInfo.InvariantCulture),
                    row.PrevTradingValue.ToString(CultureInfo.InvariantCulture),
                    row.AvgTradingValue20D?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.ValueToMarketCapPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.TradingValueToMarketCapPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.TurnoverRateByListedShares?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.TurnoverRateByFloatingShares?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.CloseLocationPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.UpperTailPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.IsBollingerUpperBreak?.ToString().ToLowerInvariant() ?? string.Empty,
                    row.PrevHighBreak?.ToString().ToLowerInvariant() ?? string.Empty,
                    Escape(row.FundamentalStatus),
                    Escape(row.DailyMetricsStatus),
                    Escape(row.MarketLogicStatus),
                    Escape(row.ScoreStatus),
                    Escape(row.MissingFieldMemo)
                }));
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            string text = value ?? string.Empty;
            if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n'))
                return text;

            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
                return Path.Combine(Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory(), RelativeRoot.Replace('/', Path.DirectorySeparatorChar));

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
                return Path.Combine(Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory, RelativeRoot.Replace('/', Path.DirectorySeparatorChar));

            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot.Replace('/', Path.DirectorySeparatorChar));
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
    }
}
