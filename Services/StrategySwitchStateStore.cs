using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class StrategySwitchStateStore
    {
        private const string RelativeRoot = "Storage/StrategySlots";
        private const string FileName = "switch_state.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public StrategySwitchStateStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public IReadOnlyList<StrategySwitchStateEntry> Load()
        {
            lock (_sync)
            {
                string path = BuildPath();
                if (!File.Exists(path))
                    return [];

                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    return JsonSerializer.Deserialize<List<StrategySwitchStateEntry>>(json, JsonOptions) ?? [];
                }
                catch
                {
                    return [];
                }
            }
        }

        public void Save(IEnumerable<StrategySwitchStateEntry> entries)
        {
            lock (_sync)
            {
                List<StrategySwitchStateEntry> rows = [.. (entries ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                    .GroupBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => x.Last())
                    .OrderBy(x => x.Key, StringComparer.Ordinal)];

                Directory.CreateDirectory(_rootPath);
                File.WriteAllText(BuildPath(), JsonSerializer.Serialize(rows, JsonOptions), Encoding.UTF8);
            }
        }

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
