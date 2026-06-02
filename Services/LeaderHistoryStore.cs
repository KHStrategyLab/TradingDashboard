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
    public sealed class LeaderHistoryStore
    {
        private const string RelativeRoot = "Storage/LeaderHistory";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly string _rootPath;

        public LeaderHistoryStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public string ActivePath => Path.Combine(_rootPath, "active_leaders.json");

        public IReadOnlyList<LeaderHistoryEntry> LoadActive()
        {
            if (!File.Exists(ActivePath))
                return [];

            try
            {
                string json = File.ReadAllText(ActivePath);
                return JsonSerializer.Deserialize<List<LeaderHistoryEntry>>(json, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public void SaveActive(IEnumerable<LeaderHistoryEntry> entries)
        {
            List<LeaderHistoryEntry> rows = [.. (entries ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.BaseDate))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.QualityScore).First())
                .OrderByDescending(item => item.QualityScore)
                .ThenByDescending(item => item.TradingValue)
                .ThenBy(item => item.Code)];
            ApplyRanks(rows);

            SaveJson(ActivePath, rows);
            SaveActiveCsv(Path.Combine(_rootPath, "active_leaders.csv"), rows);
        }

        public void SaveArchiveSnapshot(IEnumerable<LeaderHistoryEntry> entries, string runId)
        {
            string safeRunId = string.IsNullOrWhiteSpace(runId) ? DateTime.Now.ToString("yyyyMMddHHmmss") : runId;
            string path = Path.Combine(_rootPath, "archive", $"leaders_{safeRunId}.json");
            SaveJson(path, entries?.ToList() ?? []);
        }

        public void SaveSummary(LeaderHistoryRebuildSummary summary)
        {
            SaveJson(Path.Combine(_rootPath, "last_rebuild_summary.json"), summary);
            SaveJson(Path.Combine(_rootPath, "metadata", $"rebuild_summary_{summary.RunId}.json"), summary);
        }

        private static void SaveJson<T>(string path, T value)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }

        private static void SaveActiveCsv(string path, IReadOnlyList<LeaderHistoryEntry> rows)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var builder = new StringBuilder();
            builder.AppendLine("PriorityRank,DailyRank,Code,Name,Market,BaseDate,QualityGrade,QualityScore,LeaderType,Source,Status,TradingValue,ChangeRate,CloseLocationPercent,UpperTailPercent,BollingerUpperBreak,PrevHighPlus10,MarketCap,ValueToMarketCapPercent,TurnoverRate,KrxClose,NxtClose,QualityReason");
            foreach (LeaderHistoryEntry row in rows)
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    row.PriorityRank.ToString(CultureInfo.InvariantCulture),
                    row.DailyRank.ToString(CultureInfo.InvariantCulture),
                    Escape(row.Code),
                    Escape(row.Name),
                    Escape(row.Market),
                    Escape(row.BaseDate),
                    Escape(row.QualityGrade),
                    row.QualityScore.ToString("0.##", CultureInfo.InvariantCulture),
                    Escape(row.LeaderType),
                    Escape(row.Source),
                    Escape(row.Status),
                    row.TradingValue.ToString(CultureInfo.InvariantCulture),
                    row.ChangeRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.CloseLocationPercent.ToString("0.##", CultureInfo.InvariantCulture),
                    row.UpperTailPercent.ToString("0.##", CultureInfo.InvariantCulture),
                    row.BollingerUpperBreak ? "true" : "false",
                    row.PrevHighPlus10 ? "true" : "false",
                    row.MarketCap?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.ValueToMarketCapPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.TurnoverRate?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.KrxClose.ToString(CultureInfo.InvariantCulture),
                    row.NxtClose > 0 ? row.NxtClose.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    Escape(row.QualityReason)
                }));
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }

        private static void ApplyRanks(IReadOnlyList<LeaderHistoryEntry> rows)
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i].PriorityRank = i + 1;

            foreach (IGrouping<string, LeaderHistoryEntry> group in rows
                .GroupBy(item => item.BaseDate, StringComparer.Ordinal))
            {
                int dailyRank = 1;
                foreach (LeaderHistoryEntry row in group
                    .OrderByDescending(item => item.QualityScore)
                    .ThenByDescending(item => item.TradingValue)
                    .ThenBy(item => item.Code))
                {
                    row.DailyRank = dailyRank++;
                }
            }
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
