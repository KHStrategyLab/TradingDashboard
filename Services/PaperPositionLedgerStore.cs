using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class PaperPositionLedgerStore
    {
        private const string RelativeRoot = "Storage/PaperPositions";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public PaperPositionLedgerStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public IReadOnlyList<PaperPositionLedgerEntry> LoadToday()
        {
            lock (_sync)
            {
                string path = BuildPath(DateTime.Now);
                if (!File.Exists(path))
                    return [];

                try
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<PaperPositionLedgerEntry>>(json, JsonOptions) ?? [];
                }
                catch
                {
                    return [];
                }
            }
        }

        public void SaveToday(IEnumerable<PaperPositionLedgerEntry> entries)
        {
            lock (_sync)
            {
                string path = BuildPath(DateTime.Now);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                List<PaperPositionLedgerEntry> rows = [.. (entries ?? [])
                    .Where(entry => entry != null)
                    .OrderBy(entry => entry.Status)
                    .ThenBy(entry => entry.Code)];

                File.WriteAllText(path, JsonSerializer.Serialize(rows, JsonOptions));
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
                return Path.Combine(root, "Storage", "PaperPositions");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "PaperPositions");
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
