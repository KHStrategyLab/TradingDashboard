using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;
using TradingDashboard.Services.Strategies;

namespace TradingDashboard.Services
{
    public sealed class StrategySlotConfigStore
    {
        private const string RelativeRoot = "Storage/StrategySlots";
        private const string FileName = "slot_config.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public StrategySlotConfigStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public IReadOnlyList<StrategySlotConfigEntry> Load()
        {
            lock (_sync)
            {
                string path = BuildPath();
                if (!File.Exists(path))
                    return CreateDefaults();

                try
                {
                    string json = File.ReadAllText(path);
                    List<StrategySlotConfigEntry> loaded = JsonSerializer.Deserialize<List<StrategySlotConfigEntry>>(json, JsonOptions) ?? [];
                    return MergeDefaults(loaded);
                }
                catch
                {
                    return CreateDefaults();
                }
            }
        }

        public void Save(IEnumerable<StrategySlotConfigEntry> entries)
        {
            lock (_sync)
            {
                List<StrategySlotConfigEntry> normalized = MergeDefaults(entries ?? []);
                string? directory = Path.GetDirectoryName(BuildPath());
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(BuildPath(), JsonSerializer.Serialize(normalized, JsonOptions));
            }
        }

        private static List<StrategySlotConfigEntry> MergeDefaults(IEnumerable<StrategySlotConfigEntry> entries)
        {
            Dictionary<string, StrategySlotConfigEntry> bySlot = entries
                .Where(x => !string.IsNullOrWhiteSpace(x.SlotId))
                .GroupBy(x => x.SlotId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);

            foreach (StrategySlotId slotId in Enum.GetValues<StrategySlotId>())
            {
                string key = slotId.ToString();
                if (!bySlot.TryGetValue(key, out StrategySlotConfigEntry? entry))
                {
                    bySlot[key] = new StrategySlotConfigEntry
                    {
                        SlotId = key,
                        IsEnabled = GetDefaultEnabledForSlot(slotId),
                        ExitStrategyCode = StrategyExitStrategyRegistry.GetDefaultForSlot(slotId),
                        UpdatedAt = DateTime.Now.ToString("yyyyMMddHHmmss")
                    };
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ExitStrategyCode))
                    entry.ExitStrategyCode = StrategyExitStrategyRegistry.GetDefaultForSlot(slotId);
                entry.IsEnabled ??= GetDefaultEnabledForSlot(slotId);
            }

            return [.. bySlot.Values.OrderBy(x => x.SlotId, StringComparer.Ordinal)];
        }

        private static bool GetDefaultEnabledForSlot(StrategySlotId slotId) =>
            slotId != StrategySlotId.ThemeDisclosureAssist &&
            slotId != StrategySlotId.IntradayFifteenMinuteScalp &&
            slotId != StrategySlotId.IntradayFiveMinuteStableScalp;

        private static List<StrategySlotConfigEntry> CreateDefaults() =>
            MergeDefaults([]);

        private string BuildPath() =>
            Path.Combine(_rootPath, FileName);

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
            {
                string root = Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Storage", "StrategySlots");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "StrategySlots");
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
