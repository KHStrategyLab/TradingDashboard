using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteCacheService
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<string, StrategyStockMinuteCache> _stockCaches = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string code, string market, int minute, IEnumerable<DailyCandle> candles, int targetCount)
        {
            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            if (string.IsNullOrWhiteSpace(normalizedCode) || minute <= 0)
                return;

            List<StrategyMinuteBar> bars = [.. (candles ?? [])
                .Select(ToMinuteBar)
                .Where(bar => bar != null && bar.Close > 0 && bar.BucketTime != DateTime.MinValue)
                .Select(bar => bar!)
                .GroupBy(bar => bar.BucketTime)
                .Select(group => group.First())
                .OrderBy(bar => bar.BucketTime)];

            if (bars.Count == 0)
                return;

            lock (_syncRoot)
            {
                StrategyMinuteBlock block = GetOrCreateBlock(normalizedCode, normalizedMarket, minute);
                block.Seed(bars, targetCount);
            }
        }

        public void ApplyClosedBar(string code, string market, int minute, DailyCandle candle, int targetCount = 0)
        {
            StrategyMinuteBar? bar = ToMinuteBar(candle);
            if (bar == null)
                return;

            ApplyClosedBar(
                code,
                market,
                minute,
                bar.BucketTime,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume,
                bar.TradingValue,
                targetCount);
        }

        public void ApplyClosedBar(
            string code,
            string market,
            int minute,
            DateTime bucketTime,
            long open,
            long high,
            long low,
            long close,
            long volume,
            long tradingValue,
            int targetCount = 0)
        {
            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            if (string.IsNullOrWhiteSpace(normalizedCode) || minute <= 0 || bucketTime == DateTime.MinValue || close <= 0)
                return;

            lock (_syncRoot)
            {
                long safeVolume = Math.Max(0, volume);
                long safeTradingValue = tradingValue > 0
                    ? tradingValue
                    : (long)Math.Min(long.MaxValue, close * (double)safeVolume);

                StrategyMinuteBar closedBar = new()
                {
                    BucketTime = bucketTime,
                    Open = open > 0 ? open : close,
                    High = high > 0 ? high : close,
                    Low = low > 0 ? low : close,
                    Close = close,
                    Volume = safeVolume,
                    TradingValue = safeTradingValue
                };

                StrategyMinuteBlock block = GetOrCreateBlock(normalizedCode, normalizedMarket, minute);
                block.ApplyClosedBar(closedBar, targetCount);
            }
        }

        public void ApplyRealtimeTick(string code, string market, long price, long tradeVolume, long tradeValue, DateTime tradeTime)
        {
            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            if (string.IsNullOrWhiteSpace(normalizedCode) || price <= 0)
                return;

            lock (_syncRoot)
            {
                if (!_stockCaches.TryGetValue(BuildStockKey(normalizedCode, normalizedMarket), out StrategyStockMinuteCache? stockCache))
                    return;

                foreach (StrategyMinuteBlock block in stockCache.Frames.Values)
                {
                    if (!block.IsReady)
                        continue;

                    block.ApplyRealtimeTick(price, tradeVolume, tradeValue, tradeTime);
                }
            }
        }

        public StrategyMinuteDataStatus BuildStatus(string code, string market)
        {
            var status = new StrategyMinuteDataStatus();
            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            if (string.IsNullOrWhiteSpace(normalizedCode))
                return status;

            lock (_syncRoot)
            {
                if (!_stockCaches.TryGetValue(BuildStockKey(normalizedCode, normalizedMarket), out StrategyStockMinuteCache? stockCache))
                    return status;

                foreach (StrategyMinuteBlock block in stockCache.Frames.Values)
                    status.SetCount(block.Minute, block.Count, block.TargetCount);
            }

            return status;
        }

        public StrategyMinuteSnapshotSet GetSnapshotSet(string code, string market, params int[] minutes)
        {
            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            List<StrategyMinuteFrameSnapshot> frames = [];

            lock (_syncRoot)
            {
                if (_stockCaches.TryGetValue(BuildStockKey(normalizedCode, normalizedMarket), out StrategyStockMinuteCache? stockCache))
                {
                    IEnumerable<int> targetMinutes = minutes.Length == 0
                        ? stockCache.Frames.Keys
                        : minutes;

                    foreach (int minute in targetMinutes)
                    {
                        if (stockCache.Frames.TryGetValue(minute, out StrategyMinuteBlock? block))
                            frames.Add(block.CreateSnapshot());
                    }
                }
            }

            return new StrategyMinuteSnapshotSet(normalizedCode, normalizedMarket, frames);
        }

        public bool TryGetLastMa60TouchAnchor(
            string code,
            string market,
            int minute,
            DateTime baseDate,
            out StrategyMa60TouchAnchor anchor)
        {
            anchor = new StrategyMa60TouchAnchor();
            string normalizedCode = NormalizeCode(code);
            string normalizedMarket = NormalizeMarket(market);
            if (string.IsNullOrWhiteSpace(normalizedCode) || minute <= 0 || baseDate == DateTime.MinValue)
                return false;

            lock (_syncRoot)
            {
                if (!_stockCaches.TryGetValue(BuildStockKey(normalizedCode, normalizedMarket), out StrategyStockMinuteCache? stockCache) ||
                    !stockCache.Frames.TryGetValue(minute, out StrategyMinuteBlock? block))
                    return false;

                return block.TryGetLastMa60TouchAnchor(baseDate, out anchor);
            }
        }

        private StrategyMinuteBlock GetOrCreateBlock(string code, string market, int minute)
        {
            string stockKey = BuildStockKey(code, market);
            if (!_stockCaches.TryGetValue(stockKey, out StrategyStockMinuteCache? stockCache))
            {
                stockCache = new StrategyStockMinuteCache(code, market);
                _stockCaches[stockKey] = stockCache;
            }

            if (!stockCache.Frames.TryGetValue(minute, out StrategyMinuteBlock? block))
            {
                block = new StrategyMinuteBlock(code, market, minute);
                stockCache.Frames[minute] = block;
            }

            return block;
        }

        private static StrategyMinuteBar? ToMinuteBar(DailyCandle candle)
        {
            DateTime time = ParseCandleTime(candle.Date);
            if (time == DateTime.MinValue)
                return null;

            long close = ToLong(candle.Close);
            if (close <= 0)
                return null;

            long open = ToLong(candle.Open);
            long high = ToLong(candle.High);
            long low = ToLong(candle.Low);
            long volume = Math.Max(0, candle.Volume);
            long tradingValue = candle.TradingValue > 0
                ? candle.TradingValue
                : (long)Math.Min(long.MaxValue, close * (double)volume);

            return new StrategyMinuteBar
            {
                BucketTime = time,
                Open = open > 0 ? open : close,
                High = high > 0 ? high : close,
                Low = low > 0 ? low : close,
                Close = close,
                Volume = volume,
                TradingValue = tradingValue
            };
        }

        private static DateTime ParseCandleTime(string text)
        {
            string digits = new([.. (text ?? string.Empty).Where(char.IsDigit)]);
            string[] formats = digits.Length switch
            {
                >= 14 => ["yyyyMMddHHmmss"],
                >= 12 => ["yyyyMMddHHmm"],
                >= 8 => ["yyyyMMdd"],
                _ => []
            };

            if (formats.Length == 0)
                return DateTime.MinValue;

            string normalized = digits.Length >= 14
                ? digits[..14]
                : digits.Length >= 12
                    ? digits[..12]
                    : digits[..8];

            return DateTime.TryParseExact(
                normalized,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime result)
                ? result
                : DateTime.MinValue;
        }

        private static long ToLong(double value) =>
            value > 0 ? (long)Math.Round(value, MidpointRounding.AwayFromZero) : 0;

        private static string NormalizeCode(string code)
        {
            string text = (code ?? string.Empty).Trim();
            text = text.Replace("_AL", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_NX", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (text.StartsWith("A", StringComparison.OrdinalIgnoreCase) && text.Length > 1)
                text = text[1..];
            return new string([.. text.Where(char.IsDigit)]);
        }

        private static string NormalizeMarket(string market) =>
            string.Equals((market ?? string.Empty).Trim(), "NXT", StringComparison.OrdinalIgnoreCase)
                ? "NXT"
                : "KRX";

        private static string BuildStockKey(string code, string market) =>
            $"{code}|{NormalizeMarket(market)}";

        private sealed class StrategyStockMinuteCache(string code, string market)
        {
            public string Code { get; } = code;
            public string Market { get; } = market;
            public Dictionary<int, StrategyMinuteBlock> Frames { get; } = [];
        }
    }
}
