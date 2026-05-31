using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class StrategyAnchorStore
    {
        private const string RelativeRoot = "Storage/StrategyAnchors";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public StrategyAnchorStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public void Save(StrategyAnchorDocument document)
        {
            if (document == null ||
                string.IsNullOrWhiteSpace(document.Code) ||
                string.IsNullOrWhiteSpace(document.BaseDate) ||
                document.Ma60Touches.Count == 0)
                return;

            lock (_sync)
            {
                string path = BuildPath(document.BaseDate, document.Code, document.Market);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
            }
        }

        private string BuildPath(string baseDate, string code, string market)
        {
            string directory = Path.Combine(_rootPath, NormalizeDate(baseDate));
            string fileName = $"{NormalizeCode(code)}_{NormalizeMarket(market)}.json";
            return Path.Combine(directory, fileName);
        }

        private static string NormalizeCode(string code)
        {
            string text = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (text.StartsWith("A", StringComparison.OrdinalIgnoreCase) && text.Length >= 7)
                text = text[1..];
            if (text.EndsWith("_NX", StringComparison.OrdinalIgnoreCase))
                text = text[..^3];
            if (text.EndsWith("_AL", StringComparison.OrdinalIgnoreCase))
                text = text[..^3];
            return text;
        }

        private static string NormalizeMarket(string market) =>
            string.Equals((market ?? string.Empty).Trim(), "NXT", StringComparison.OrdinalIgnoreCase)
                ? "NXT"
                : "KRX";

        private static string NormalizeDate(string date)
        {
            string digits = new([.. (date ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 8 ? digits[..8] : DateTime.Now.ToString("yyyyMMdd");
        }

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
            {
                string root = Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Storage", "StrategyAnchors");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "StrategyAnchors");
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
