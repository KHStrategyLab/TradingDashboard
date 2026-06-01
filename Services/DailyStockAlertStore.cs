using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TradingDashboard.Services
{
    public sealed class DailyStockAlertStore
    {
        private const string RelativeRoot = "Storage/Alerts";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public DailyStockAlertStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public bool TryReserveToday(string alertKind, string code)
        {
            string safeKind = NormalizeAlertKind(alertKind);
            string safeCode = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(safeKind) || string.IsNullOrWhiteSpace(safeCode))
                return false;

            lock (_sync)
            {
                DailyStockAlertDocument document = LoadToday(safeKind);
                if (document.Codes.Contains(safeCode, StringComparer.Ordinal))
                    return false;

                document.Codes.Add(safeCode);
                SaveToday(safeKind, document);
                return true;
            }
        }

        public void ReleaseToday(string alertKind, string code)
        {
            string safeKind = NormalizeAlertKind(alertKind);
            string safeCode = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(safeKind) || string.IsNullOrWhiteSpace(safeCode))
                return;

            lock (_sync)
            {
                DailyStockAlertDocument document = LoadToday(safeKind);
                if (!document.Codes.Remove(safeCode))
                    return;

                SaveToday(safeKind, document);
            }
        }

        private DailyStockAlertDocument LoadToday(string alertKind)
        {
            string path = BuildPath(DateTime.Today, alertKind);
            if (!File.Exists(path))
            {
                return new DailyStockAlertDocument
                {
                    Date = DateTime.Today.ToString("yyyyMMdd"),
                    AlertKind = alertKind
                };
            }

            try
            {
                string json = File.ReadAllText(path);
                DailyStockAlertDocument document = JsonSerializer.Deserialize<DailyStockAlertDocument>(json, JsonOptions) ?? new DailyStockAlertDocument();
                document.Date = string.IsNullOrWhiteSpace(document.Date) ? DateTime.Today.ToString("yyyyMMdd") : document.Date;
                document.AlertKind = string.IsNullOrWhiteSpace(document.AlertKind) ? alertKind : document.AlertKind;
                document.Codes = [.. document.Codes
                    .Select(NormalizeCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)];
                return document;
            }
            catch
            {
                return new DailyStockAlertDocument
                {
                    Date = DateTime.Today.ToString("yyyyMMdd"),
                    AlertKind = alertKind
                };
            }
        }

        private void SaveToday(string alertKind, DailyStockAlertDocument document)
        {
            Directory.CreateDirectory(_rootPath);
            document.Date = DateTime.Today.ToString("yyyyMMdd");
            document.AlertKind = alertKind;
            document.UpdatedAt = DateTime.Now.ToString("yyyyMMddHHmmss");
            document.Codes = [.. document.Codes
                .Select(NormalizeCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)];

            File.WriteAllText(BuildPath(DateTime.Today, alertKind), JsonSerializer.Serialize(document, JsonOptions));
        }

        private string BuildPath(DateTime date, string alertKind) =>
            Path.Combine(_rootPath, $"{date:yyyyMMdd}_{alertKind}.json");

        private static string NormalizeAlertKind(string value)
        {
            string normalized = new([.. (value ?? string.Empty)
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')]);
            return normalized.Trim().ToLowerInvariant();
        }

        private static string NormalizeCode(string value)
        {
            string digits = new([.. (value ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length > 6 ? digits[^6..] : digits;
        }

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
            {
                string root = Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Storage", "Alerts");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "Alerts");
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

        private sealed class DailyStockAlertDocument
        {
            public string Date { get; set; } = string.Empty;
            public string AlertKind { get; set; } = string.Empty;
            public List<string> Codes { get; set; } = [];
            public string UpdatedAt { get; set; } = string.Empty;
        }
    }
}
