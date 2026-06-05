using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestDataStore
    {
        private const string RelativeRoot = "Storage/Backtests/DataStore";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _rootPath;

        public BacktestDataStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public string RootPath => _rootPath;

        public IReadOnlyList<BacktestDailyBar> LoadDailyBars(string code, string market)
        {
            lock (_sync)
                return LoadList<BacktestDailyBar>(BuildDailyPath(code, market));
        }

        public IReadOnlyList<BacktestMinuteBar> LoadMinuteBars(string code, string market, int minute)
        {
            lock (_sync)
                return LoadList<BacktestMinuteBar>(BuildMinutePath(code, market, minute));
        }

        public int UpsertDailyBars(string code, string market, IEnumerable<BacktestDailyBar> bars)
        {
            List<BacktestDailyBar> incoming = [.. (bars ?? [])
                .Where(bar => bar != null && !string.IsNullOrWhiteSpace(bar.Date))];
            if (incoming.Count == 0)
                return 0;

            lock (_sync)
            {
                string path = BuildDailyPath(code, market);
                Dictionary<string, BacktestDailyBar> merged = LoadList<BacktestDailyBar>(path)
                    .Where(bar => !string.IsNullOrWhiteSpace(bar.Date))
                    .ToDictionary(bar => bar.Date, StringComparer.Ordinal);

                string now = DateTime.Now.ToString("yyyyMMddHHmmss");
                foreach (BacktestDailyBar bar in incoming)
                {
                    bar.Code = NormalizeCode(string.IsNullOrWhiteSpace(bar.Code) ? code : bar.Code);
                    bar.Market = NormalizeMarket(string.IsNullOrWhiteSpace(bar.Market) ? market : bar.Market);
                    bar.UpdatedAt = now;
                    merged[bar.Date] = bar;
                }

                SaveList(path, merged.Values.OrderBy(bar => bar.Date).ToList());
                return incoming.Count;
            }
        }

        public IReadOnlyList<BacktestBaseCandle> LoadBaseCandles()
        {
            lock (_sync)
                return LoadList<BacktestBaseCandle>(BuildBaseCandlePath());
        }

        public int UpsertMinuteBars(string code, string market, int minute, IEnumerable<BacktestMinuteBar> bars)
        {
            List<BacktestMinuteBar> incoming = [.. (bars ?? [])
                .Where(bar => bar != null && !string.IsNullOrWhiteSpace(bar.DateTime))];
            if (incoming.Count == 0 || minute <= 0)
                return 0;

            lock (_sync)
            {
                string path = BuildMinutePath(code, market, minute);
                Dictionary<string, BacktestMinuteBar> merged = LoadList<BacktestMinuteBar>(path)
                    .Where(bar => !string.IsNullOrWhiteSpace(bar.DateTime))
                    .ToDictionary(bar => bar.DateTime, StringComparer.Ordinal);

                string now = DateTime.Now.ToString("yyyyMMddHHmmss");
                foreach (BacktestMinuteBar bar in incoming)
                {
                    bar.Code = NormalizeCode(string.IsNullOrWhiteSpace(bar.Code) ? code : bar.Code);
                    bar.Market = NormalizeMarket(string.IsNullOrWhiteSpace(bar.Market) ? market : bar.Market);
                    bar.Minute = minute;
                    bar.UpdatedAt = now;
                    merged[bar.DateTime] = bar;
                }

                SaveList(path, merged.Values.OrderBy(bar => bar.DateTime).ToList());
                return incoming.Count;
            }
        }

        public int UpsertBaseCandles(IEnumerable<BacktestBaseCandle> events)
        {
            List<BacktestBaseCandle> incoming = [.. (events ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Key))];
            if (incoming.Count == 0)
                return 0;

            lock (_sync)
            {
                string path = BuildBaseCandlePath();
                Dictionary<string, BacktestBaseCandle> merged = LoadList<BacktestBaseCandle>(path)
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .ToDictionary(item => item.Key, StringComparer.Ordinal);

                foreach (BacktestBaseCandle item in incoming)
                    merged[item.Key] = item;

                SaveList(path, merged.Values
                    .OrderBy(item => item.Code)
                    .ThenBy(item => item.Market)
                    .ThenBy(item => item.BaseCandleDate)
                    .ToList());
                return incoming.Count;
            }
        }

        public void SaveCandidates(IEnumerable<BacktestCandidate> candidates, string sourceName)
        {
            List<BacktestCandidate> rows = [.. (candidates ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{NormalizeCode(item.Code)}|{NormalizeMarket(item.Market)}|{NormalizeDate(item.CandidateDate)}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Code)];

            lock (_sync)
                SaveList(BuildCandidatePath(sourceName), rows);
        }

        public string BuildDailyPath(string code, string market) =>
            Path.Combine(_rootPath, "daily", $"{NormalizeCode(code)}_{NormalizeMarket(market)}_daily.json");

        public string BuildMinutePath(string code, string market, int minute) =>
            Path.Combine(_rootPath, "minute", $"{Math.Max(1, minute)}m", $"{NormalizeCode(code)}_{NormalizeMarket(market)}_{Math.Max(1, minute)}m.json");

        private string BuildBaseCandlePath() =>
            Path.Combine(_rootPath, "base_candles", "verified_base_candles.json");

        private string BuildCandidatePath(string sourceName)
        {
            string safeName = string.IsNullOrWhiteSpace(sourceName) ? "candidates" : SanitizeFileName(sourceName);
            return Path.Combine(_rootPath, "metadata", $"{safeName}.json");
        }

        private static List<T> LoadList<T>(string path)
        {
            if (!File.Exists(path))
                return [];

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static void SaveList<T>(string path, List<T> rows)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(rows, JsonOptions));
        }

        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            string text = code.Trim().ToUpperInvariant();
            if (text.StartsWith("A", StringComparison.OrdinalIgnoreCase) && text.Length >= 7)
                text = text[1..];
            if (text.EndsWith("_NX", StringComparison.OrdinalIgnoreCase))
                text = text[..^3];
            if (text.EndsWith("_AL", StringComparison.OrdinalIgnoreCase))
                text = text[..^3];
            string digits = new([.. text.Where(char.IsDigit)]);
            return digits.Length >= 6 ? digits[^6..] : digits;
        }

        public static string NormalizeMarket(string market)
        {
            string value = (market ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Contains("AL", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("SOR", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("UNIFIED", StringComparison.OrdinalIgnoreCase))
                return "AL";
            if (value.Contains("NXT", StringComparison.OrdinalIgnoreCase))
                return "NXT";
            return "KRX";
        }

        public static string NormalizeDate(string date)
        {
            string digits = new([.. (date ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 8)
                return digits[..8];
            return DateTime.Today.ToString("yyyyMMdd");
        }

        private static string SanitizeFileName(string value)
        {
            string text = string.Join("_", (value ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(text) ? "candidates" : text;
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
