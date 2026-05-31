using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class StrategyOrderJournalStore
    {
        private const string RelativeRoot = "Storage/StrategyOrderJournal";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public StrategyOrderJournalStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public IReadOnlyList<StrategyOrderJournalEntry> LoadToday()
        {
            lock (_sync)
            {
                string path = BuildPath(DateTime.Now);
                if (!File.Exists(path))
                    return [];

                try
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<StrategyOrderJournalEntry>>(json, JsonOptions) ?? [];
                }
                catch
                {
                    return [];
                }
            }
        }

        public void UpsertToday(StrategyOrderJournalEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                return;

            lock (_sync)
            {
                string path = BuildPath(DateTime.Now);
                List<StrategyOrderJournalEntry> entries = [];
                if (File.Exists(path))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        entries = JsonSerializer.Deserialize<List<StrategyOrderJournalEntry>>(json, JsonOptions) ?? [];
                    }
                    catch
                    {
                        entries = [];
                    }
                }

                entry.Date = string.IsNullOrWhiteSpace(entry.Date) ? DateTime.Now.ToString("yyyyMMdd") : entry.Date;
                entry.SavedAt = DateTime.Now.ToString("yyyyMMddHHmmss");

                int index = entries.FindIndex(item => string.Equals(item.Key, entry.Key, StringComparison.Ordinal));
                if (index >= 0)
                {
                    StrategyOrderJournalEntry existing = entries[index];
                    if (entry.Quantity <= 0)
                        entry.Quantity = existing.Quantity;
                    if (entry.ReferencePrice <= 0)
                        entry.ReferencePrice = existing.ReferencePrice;
                    if (entry.OrderPrice <= 0)
                        entry.OrderPrice = existing.OrderPrice;
                    if (string.IsNullOrWhiteSpace(entry.OrderNo))
                        entry.OrderNo = existing.OrderNo;
                    if (string.IsNullOrWhiteSpace(entry.ReturnMessage))
                        entry.ReturnMessage = existing.ReturnMessage;
                    if (string.IsNullOrWhiteSpace(entry.SlotId))
                        entry.SlotId = existing.SlotId;

                    entries[index] = entry;
                }
                else
                {
                    entries.Add(entry);
                }

                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonSerializer.Serialize(entries.OrderBy(x => x.SavedAt).ToList(), JsonOptions));
            }
        }

        private string BuildPath(DateTime date)
        {
            return Path.Combine(_rootPath, $"{date:yyyyMMdd}.json");
        }

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
            {
                string root = Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Storage", "StrategyOrderJournal");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "StrategyOrderJournal");
            }

            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot);
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
