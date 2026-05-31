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
                StrategyMinuteFrameCache frame = GetOrCreateFrame(normalizedCode, normalizedMarket, minute);
                frame.TargetCount = Math.Max(1, targetCount);
                frame.CompletedBars = [.. bars.TakeLast(frame.TargetCount)];
                frame.CurrentBar = null;
                frame.LoadedAt = DateTime.Now;
                RecalculateFrame(frame);
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
                StrategyMinuteFrameCache frame = GetOrCreateFrame(normalizedCode, normalizedMarket, minute);
                if (targetCount > 0)
                    frame.TargetCount = Math.Max(frame.TargetCount, targetCount);

                DateTime closedBucket = FloorMinute(bucketTime, minute);
                long safeVolume = Math.Max(0, volume);
                long safeTradingValue = tradingValue > 0
                    ? tradingValue
                    : (long)Math.Min(long.MaxValue, close * (double)safeVolume);

                StrategyMinuteBar closedBar = new()
                {
                    BucketTime = closedBucket,
                    Open = open > 0 ? open : close,
                    High = high > 0 ? high : close,
                    Low = low > 0 ? low : close,
                    Close = close,
                    Volume = safeVolume,
                    TradingValue = safeTradingValue
                };

                int existingIndex = frame.CompletedBars.FindIndex(bar => bar.BucketTime == closedBucket);
                if (existingIndex >= 0)
                    frame.CompletedBars[existingIndex] = closedBar;
                else
                    frame.CompletedBars.Add(closedBar);

                if (frame.CurrentBar?.BucketTime == closedBucket)
                    frame.CurrentBar = null;

                RecalculateFrame(frame);
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

                foreach (StrategyMinuteFrameCache frame in stockCache.Frames.Values)
                {
                    if (!frame.IsReady)
                        continue;

                    ApplyRealtimeTickToFrame(frame, price, tradeVolume, tradeValue, tradeTime);
                    RecalculateFrame(frame);
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

                foreach (StrategyMinuteFrameCache frame in stockCache.Frames.Values)
                    status.SetCount(frame.Minute, frame.CompletedBars.Count + (frame.CurrentBar == null ? 0 : 1), frame.TargetCount);
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
                        if (stockCache.Frames.TryGetValue(minute, out StrategyMinuteFrameCache? frame))
                            frames.Add(CreateSnapshot(frame));
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
                    !stockCache.Frames.TryGetValue(minute, out StrategyMinuteFrameCache? frame))
                    return false;

                StrategyMinuteBar? touch = frame.CompletedBars
                    .Where(bar => bar.Ma60 > 0 &&
                        bar.BucketTime.Date == baseDate.Date &&
                        bar.Low <= bar.Ma60 &&
                        bar.High >= bar.Ma60)
                    .OrderBy(bar => bar.BucketTime)
                    .LastOrDefault();

                if (touch == null)
                    return false;

                anchor = new StrategyMa60TouchAnchor
                {
                    Minute = minute,
                    Time = touch.BucketTime,
                    TouchPrice = (long)Math.Round(touch.Ma60, MidpointRounding.AwayFromZero),
                    Ma60 = touch.Ma60,
                    Open = touch.Open,
                    High = touch.High,
                    Low = touch.Low,
                    Close = touch.Close,
                    Volume = touch.Volume,
                    TradingValue = touch.TradingValue
                };
                return true;
            }
        }

        private StrategyMinuteFrameCache GetOrCreateFrame(string code, string market, int minute)
        {
            string stockKey = BuildStockKey(code, market);
            if (!_stockCaches.TryGetValue(stockKey, out StrategyStockMinuteCache? stockCache))
            {
                stockCache = new StrategyStockMinuteCache(code, market);
                _stockCaches[stockKey] = stockCache;
            }

            if (!stockCache.Frames.TryGetValue(minute, out StrategyMinuteFrameCache? frame))
            {
                frame = new StrategyMinuteFrameCache(code, market, minute);
                stockCache.Frames[minute] = frame;
            }

            return frame;
        }

        private static void ApplyRealtimeTickToFrame(StrategyMinuteFrameCache frame, long price, long tradeVolume, long tradeValue, DateTime tradeTime)
        {
            DateTime sourceTime = tradeTime == DateTime.MinValue ? DateTime.Now : tradeTime;
            DateTime bucketTime = FloorMinute(sourceTime, frame.Minute);
            long safeTradeVolume = Math.Max(0, tradeVolume);
            long safeTradeValue = tradeValue > 0
                ? tradeValue
                : (long)Math.Min(long.MaxValue, price * (double)safeTradeVolume);

            if (frame.CurrentBar != null && frame.CurrentBar.BucketTime < bucketTime)
            {
                frame.CompletedBars.Add(frame.CurrentBar);
                frame.CurrentBar = null;
                TrimCompletedBars(frame);
            }

            if (frame.CurrentBar == null || frame.CurrentBar.BucketTime != bucketTime)
            {
                frame.CurrentBar = new StrategyMinuteBar
                {
                    BucketTime = bucketTime,
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    Volume = safeTradeVolume,
                    TradingValue = safeTradeValue
                };
            }
            else
            {
                StrategyMinuteBar current = frame.CurrentBar;
                if (current.Open <= 0)
                    current.Open = price;
                current.High = Math.Max(current.High > 0 ? current.High : price, price);
                current.Low = current.Low > 0 ? Math.Min(current.Low, price) : price;
                current.Close = price;
                current.Volume += safeTradeVolume;
                current.TradingValue += safeTradeValue;
            }

            frame.LastRealtimeAt = sourceTime;
        }

        private static StrategyMinuteFrameSnapshot CreateSnapshot(StrategyMinuteFrameCache frame)
        {
            StrategyMinuteBar? current = frame.CurrentBar;
            StrategyMinuteBar? completed = frame.CompletedBars.LastOrDefault();
            List<StrategyMinuteBar> lookback20 = [.. frame.CompletedBars.TakeLast(20)];
            List<long> displayCloses = BuildDisplayCloses(frame);

            return new StrategyMinuteFrameSnapshot
            {
                Code = frame.Code,
                Market = frame.Market,
                Minute = frame.Minute,
                IsReady = frame.IsReady,
                CompletedCount = frame.CompletedBars.Count,
                TargetCount = frame.TargetCount,
                LoadedAt = frame.LoadedAt,
                LastRealtimeAt = frame.LastRealtimeAt,

                CurrentBarTime = current?.BucketTime ?? DateTime.MinValue,
                CurrentOpen = current?.Open ?? 0,
                CurrentHigh = current?.High ?? 0,
                CurrentLow = current?.Low ?? 0,
                CurrentClose = current?.Close ?? 0,
                CurrentVolume = current?.Volume ?? 0,
                CurrentTradingValue = current?.TradingValue ?? 0,

                LastCompletedBarTime = completed?.BucketTime ?? DateTime.MinValue,
                LastCompletedOpen = completed?.Open ?? 0,
                LastCompletedHigh = completed?.High ?? 0,
                LastCompletedLow = completed?.Low ?? 0,
                LastCompletedClose = completed?.Close ?? 0,
                LastCompletedVolume = completed?.Volume ?? 0,
                LastCompletedTradingValue = completed?.TradingValue ?? 0,

                Ma5 = AverageLast(displayCloses, 5),
                Ma10 = AverageLast(displayCloses, 10),
                Ma20 = AverageLast(displayCloses, 20),
                Ma60 = AverageLast(displayCloses, 60),
                Ma200 = AverageLast(displayCloses, 200),
                Ma240 = AverageLast(displayCloses, 240),
                Ma480 = AverageLast(displayCloses, 480),

                LastCompletedMa5 = completed?.Ma5 ?? 0,
                LastCompletedMa10 = completed?.Ma10 ?? 0,
                LastCompletedMa20 = completed?.Ma20 ?? 0,
                LastCompletedMa60 = completed?.Ma60 ?? 0,
                LastCompletedMa200 = completed?.Ma200 ?? 0,
                LastCompletedMa240 = completed?.Ma240 ?? 0,
                LastCompletedMa480 = completed?.Ma480 ?? 0,

                High20 = lookback20.Select(bar => bar.High).DefaultIfEmpty(0).Max(),
                Low20 = lookback20.Select(bar => bar.Low).Where(value => value > 0).DefaultIfEmpty(0).Min(),
                HighestClose20 = lookback20.Select(bar => bar.Close).DefaultIfEmpty(0).Max(),
                LowestClose20 = lookback20.Select(bar => bar.Close).Where(value => value > 0).DefaultIfEmpty(0).Min(),
                Volume20 = SafeSum(lookback20.Select(bar => bar.Volume)),
                TradingValue20 = SafeSum(lookback20.Select(bar => bar.TradingValue))
            };
        }

        private static void RecalculateFrame(StrategyMinuteFrameCache frame)
        {
            frame.CompletedBars = [.. frame.CompletedBars
                .Where(bar => bar != null && bar.Close > 0 && bar.BucketTime != DateTime.MinValue)
                .GroupBy(bar => bar.BucketTime)
                .Select(group => group.First())
                .OrderBy(bar => bar.BucketTime)];

            TrimCompletedBars(frame);

            List<long> closes = [];
            for (int i = 0; i < frame.CompletedBars.Count; i++)
            {
                closes.Add(frame.CompletedBars[i].Close);
                frame.CompletedBars[i].Ma5 = AverageLast(closes, 5);
                frame.CompletedBars[i].Ma10 = AverageLast(closes, 10);
                frame.CompletedBars[i].Ma20 = AverageLast(closes, 20);
                frame.CompletedBars[i].Ma60 = AverageLast(closes, 60);
                frame.CompletedBars[i].Ma200 = AverageLast(closes, 200);
                frame.CompletedBars[i].Ma240 = AverageLast(closes, 240);
                frame.CompletedBars[i].Ma480 = AverageLast(closes, 480);
            }

            frame.IsReady = frame.CompletedBars.Count >= Math.Min(frame.TargetCount, 20);
        }

        private static List<long> BuildDisplayCloses(StrategyMinuteFrameCache frame)
        {
            List<long> closes = [.. frame.CompletedBars.Select(bar => bar.Close).Where(value => value > 0)];
            if (frame.CurrentBar?.Close > 0)
                closes.Add(frame.CurrentBar.Close);
            return closes;
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

        private static DateTime FloorMinute(DateTime value, int minute)
        {
            int safeMinute = Math.Max(1, minute);
            int flooredMinute = value.Minute / safeMinute * safeMinute;
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, flooredMinute, 0);
        }

        private static double AverageLast(IReadOnlyList<long> values, int period)
        {
            if (period <= 0 || values.Count < period)
                return 0;

            return values.Skip(values.Count - period).Average();
        }

        private static long SafeSum(IEnumerable<long> values)
        {
            double sum = values.Sum(value => Math.Max(0, value));
            return (long)Math.Min(long.MaxValue, sum);
        }

        private static void TrimCompletedBars(StrategyMinuteFrameCache frame)
        {
            int retain = Math.Max(frame.TargetCount + 10, 500);
            if (frame.CompletedBars.Count > retain)
                frame.CompletedBars = [.. frame.CompletedBars.TakeLast(retain)];
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
            public Dictionary<int, StrategyMinuteFrameCache> Frames { get; } = [];
        }

        private sealed class StrategyMinuteFrameCache(string code, string market, int minute)
        {
            public string Code { get; } = code;
            public string Market { get; } = market;
            public int Minute { get; } = minute;
            public int TargetCount { get; set; } = 20;
            public bool IsReady { get; set; }
            public DateTime LoadedAt { get; set; }
            public DateTime LastRealtimeAt { get; set; }
            public List<StrategyMinuteBar> CompletedBars { get; set; } = [];
            public StrategyMinuteBar? CurrentBar { get; set; }
        }
    }
}
