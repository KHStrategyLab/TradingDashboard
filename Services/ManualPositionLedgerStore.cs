using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class ManualPositionLedgerStore
    {
        private const string RelativeRoot = "Storage/ManualPositions";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public ManualPositionLedgerStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public IReadOnlyList<ManualPositionLedgerEntry> LoadToday()
        {
            lock (_sync)
                return Load(BuildPath(DateTime.Now));
        }

        public void UpsertToday(ManualPositionLedgerEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                return;

            lock (_sync)
            {
                string path = BuildPath(DateTime.Now);
                List<ManualPositionLedgerEntry> entries = Load(path);

                entry.Date = string.IsNullOrWhiteSpace(entry.Date) ? DateTime.Now.ToString("yyyyMMdd") : entry.Date;
                entry.UpdatedAt = DateTime.Now.ToString("yyyyMMddHHmmss");

                int index = entries.FindIndex(item => string.Equals(item.Key, entry.Key, StringComparison.Ordinal));
                if (index >= 0)
                    entries[index] = Merge(entries[index], entry);
                else
                    entries.Add(entry);

                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonSerializer.Serialize(entries.OrderBy(x => x.Code).ToList(), JsonOptions));
            }
        }

        private static ManualPositionLedgerEntry Merge(ManualPositionLedgerEntry existing, ManualPositionLedgerEntry entry)
        {
            if (entry.Quantity <= 0)
                entry.Quantity = existing.Quantity;
            if (entry.OpenQuantity <= 0 &&
                !string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                entry.OpenQuantity = existing.OpenQuantity;
            if (entry.AveragePrice <= 0)
                entry.AveragePrice = existing.AveragePrice;
            if (entry.Entry5MinuteLow <= 0)
                entry.Entry5MinuteLow = existing.Entry5MinuteLow;
            if (string.IsNullOrWhiteSpace(entry.Name))
                entry.Name = existing.Name;
            if (string.IsNullOrWhiteSpace(entry.FillTime))
                entry.FillTime = existing.FillTime;
            if (string.IsNullOrWhiteSpace(entry.Entry5MinuteTime))
                entry.Entry5MinuteTime = existing.Entry5MinuteTime;
            if (string.IsNullOrWhiteSpace(entry.Status))
                entry.Status = existing.Status;
            if (string.IsNullOrWhiteSpace(entry.Memo))
                entry.Memo = existing.Memo;

            return entry;
        }

        private static List<ManualPositionLedgerEntry> Load(string path)
        {
            if (!File.Exists(path))
                return [];

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<ManualPositionLedgerEntry>>(json, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private string BuildPath(DateTime date) =>
            Path.Combine(_rootPath, $"{date:yyyyMMdd}.json");

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
            {
                string root = Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Storage", "ManualPositions");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "ManualPositions");
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
