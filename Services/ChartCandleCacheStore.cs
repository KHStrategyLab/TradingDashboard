using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class ChartCandleCacheStore
    {
        private const string RelativePath = "Config/chart_candle_cache.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _sync = new();
        private readonly string _path;
        private ChartCandleCacheDocument? _document;

        public ChartCandleCacheStore(string? path = null)
        {
            _path = string.IsNullOrWhiteSpace(path) ? ResolveDefaultPath() : path;
        }

        public bool TryGet(string code, bool useNxtMarket, string period, int count, out List<DailyCandle> candles)
        {
            lock (_sync)
            {
                ChartCandleCacheEntry? entry = LoadCore().Items.FirstOrDefault(item =>
                    string.Equals(item.Code, NormalizeCode(code), StringComparison.Ordinal) &&
                    item.UseNxtMarket == useNxtMarket &&
                    string.Equals(item.Period, period, StringComparison.OrdinalIgnoreCase));

                candles = entry?.Candles is { Count: > 0 }
                    ? [.. entry.Candles.TakeLast(Math.Max(1, count))]
                    : [];

                return candles.Count > 0;
            }
        }

        public void Upsert(string code, bool useNxtMarket, string period, IEnumerable<DailyCandle> candles)
        {
            List<DailyCandle> snapshot = [.. candles
                .Where(candle => candle != null && !string.IsNullOrWhiteSpace(candle.Date))
                .OrderBy(candle => candle.Date)];

            if (snapshot.Count == 0)
                return;

            lock (_sync)
            {
                ChartCandleCacheDocument document = LoadCore();
                string normalizedCode = NormalizeCode(code);
                ChartCandleCacheEntry? entry = document.Items.FirstOrDefault(item =>
                    string.Equals(item.Code, normalizedCode, StringComparison.Ordinal) &&
                    item.UseNxtMarket == useNxtMarket &&
                    string.Equals(item.Period, period, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    entry = new ChartCandleCacheEntry
                    {
                        Code = normalizedCode,
                        UseNxtMarket = useNxtMarket,
                        Period = period
                    };
                    document.Items.Add(entry);
                }

                entry.CachedAt = DateTime.Now.ToString("yyyyMMddHHmmss");
                entry.Candles = snapshot;
                document.Meta.UpdatedAt = entry.CachedAt;
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                ChartCandleCacheDocument document = LoadCore();
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(document, JsonOptions);
                File.WriteAllText(_path, json);
            }
        }

        private ChartCandleCacheDocument LoadCore()
        {
            if (_document != null)
                return _document;

            try
            {
                if (File.Exists(_path))
                {
                    string json = File.ReadAllText(_path);
                    _document = JsonSerializer.Deserialize<ChartCandleCacheDocument>(json, JsonOptions);
                }
            }
            catch
            {
                _document = null;
            }

            _document ??= new ChartCandleCacheDocument();
            _document.Items ??= [];
            return _document;
        }

        private static string NormalizeCode(string code)
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
            return text;
        }

        private static string ResolveDefaultPath()
        {
            string? foundFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(foundFromCurrent))
                return Path.Combine(foundFromCurrent, "chart_candle_cache.json");

            string? foundFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(foundFromBase))
                return Path.Combine(foundFromBase, "chart_candle_cache.json");

            return Path.Combine(Directory.GetCurrentDirectory(), RelativePath);
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
