using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class PaperTradeMarkStore
    {
        private const string RelativeRoot = "Storage/PaperTradeMarks";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public PaperTradeMarkStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public void AppendToday(PaperTradeMarkEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                return;

            lock (_sync)
            {
                string path = BuildPath(DateTime.Now);
                List<PaperTradeMarkEntry> entries = Load(path);
                if (!entries.Any(item => string.Equals(item.Id, entry.Id, StringComparison.Ordinal)))
                    entries.Add(entry);

                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonSerializer.Serialize(entries.OrderBy(x => x.Time).ThenBy(x => x.Id).ToList(), JsonOptions));
            }
        }

        private static List<PaperTradeMarkEntry> Load(string path)
        {
            if (!File.Exists(path))
                return [];

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<PaperTradeMarkEntry>>(json, JsonOptions) ?? [];
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
                return Path.Combine(root, "Storage", "PaperTradeMarks");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "PaperTradeMarks");
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
