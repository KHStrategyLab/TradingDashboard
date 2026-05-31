using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class StrategyMinuteSeedFileStore
    {
        private const string RelativeRoot = "Storage/StrategyMinuteSeeds";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public StrategyMinuteSeedFileStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public bool TryLoadToday(string code, string market, int minute, int targetCount, out List<DailyCandle> candles)
        {
            candles = [];
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(market) || minute <= 0)
                return false;

            lock (_sync)
            {
                string path = BuildPath(DateTime.Now, code, market, minute);
                if (!File.Exists(path))
                    return false;

                try
                {
                    string json = File.ReadAllText(path);
                    StrategyMinuteSeedFileDocument? document = JsonSerializer.Deserialize<StrategyMinuteSeedFileDocument>(json, JsonOptions);
                    if (document == null ||
                        !string.Equals(NormalizeCode(document.Code), NormalizeCode(code), StringComparison.Ordinal) ||
                        !string.Equals(NormalizeMarket(document.Market), NormalizeMarket(market), StringComparison.Ordinal) ||
                        document.Minute != minute)
                        return false;

                    candles = [.. (document.Candles ?? [])
                        .Where(candle => candle != null && !string.IsNullOrWhiteSpace(candle.Date) && candle.Close > 0)
                        .OrderBy(candle => candle.Date)
                        .TakeLast(Math.Max(1, targetCount))];

                    return candles.Count > 0;
                }
                catch
                {
                    candles = [];
                    return false;
                }
            }
        }

        public void SaveToday(string code, string market, int minute, IEnumerable<DailyCandle> candles, int targetCount)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(market) || minute <= 0)
                return;

            List<DailyCandle> snapshot = [.. (candles ?? [])
                .Where(candle => candle != null && !string.IsNullOrWhiteSpace(candle.Date) && candle.Close > 0)
                .OrderBy(candle => candle.Date)
                .TakeLast(Math.Max(1, targetCount))];

            if (snapshot.Count == 0)
                return;

            lock (_sync)
            {
                string path = BuildPath(DateTime.Now, code, market, minute);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var document = new StrategyMinuteSeedFileDocument
                {
                    Code = NormalizeCode(code),
                    Market = NormalizeMarket(market),
                    Minute = minute,
                    SeedDate = DateTime.Now.ToString("yyyyMMdd"),
                    SavedAt = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Candles = snapshot
                };

                string json = JsonSerializer.Serialize(document, JsonOptions);
                File.WriteAllText(path, json);
            }
        }

        private string BuildPath(DateTime date, string code, string market, int minute)
        {
            string directory = Path.Combine(_rootPath, date.ToString("yyyyMMdd"));
            string fileName = $"{NormalizeCode(code)}_{NormalizeMarket(market)}_{minute}.json";
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

        private static string NormalizeMarket(string market)
        {
            string text = (market ?? string.Empty).Trim().ToUpperInvariant();
            return text == "NXT" ? "NXT" : "KRX";
        }

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
            {
                string root = Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Storage", "StrategyMinuteSeeds");
            }

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
            {
                string root = Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "Storage", "StrategyMinuteSeeds");
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
