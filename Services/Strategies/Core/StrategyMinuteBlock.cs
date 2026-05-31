using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteBlock
    {
        private readonly RollingLongWindow _close5 = new(5);
        private readonly RollingLongWindow _close10 = new(10);
        private readonly RollingLongWindow _close20 = new(20);
        private readonly RollingLongWindow _close60 = new(60);
        private readonly RollingLongWindow _close200 = new(200);
        private readonly RollingLongWindow _close240 = new(240);
        private readonly RollingLongWindow _close480 = new(480);
        private readonly RollingLongWindow _high20 = new(20);
        private readonly RollingLongWindow _low20 = new(20);
        private readonly RollingLongWindow _volume20 = new(20);
        private readonly RollingLongWindow _tradingValue20 = new(20);

        public StrategyMinuteBlock(string code, string market, int minute)
        {
            Code = code;
            Market = market;
            Minute = Math.Max(1, minute);
        }

        public string Code { get; }
        public string Market { get; }
        public int Minute { get; }
        public int TargetCount { get; private set; } = 20;
        public DateTime LoadedAt { get; private set; }
        public DateTime LastRealtimeAt { get; private set; }
        public StrategyMinuteBar? CurrentBar { get; private set; }
        public List<StrategyMinuteBar> CompletedBars { get; private set; } = [];
        public int Count => CompletedBars.Count + (CurrentBar == null ? 0 : 1);
        public bool IsReady => CompletedBars.Count >= Math.Min(TargetCount, 20);

        public void Seed(IEnumerable<StrategyMinuteBar> bars, int targetCount)
        {
            List<StrategyMinuteBar> ordered = NormalizeBars(bars);
            if (ordered.Count == 0)
                return;

            TargetCount = Math.Max(Math.Max(1, targetCount), ordered.Count);
            CompletedBars = [];
            CurrentBar = null;
            ResetWindows();

            foreach (StrategyMinuteBar bar in ordered)
                AppendClosedBar(bar.Clone());

            TrimCompletedBars();
            LoadedAt = DateTime.Now;
        }

        public void ApplyClosedBar(StrategyMinuteBar bar, int targetCount = 0)
        {
            if (bar == null || bar.Close <= 0 || bar.BucketTime == DateTime.MinValue)
                return;

            if (targetCount > 0)
                TargetCount = Math.Max(TargetCount, targetCount);

            StrategyMinuteBar closedBar = bar.Clone();
            closedBar.BucketTime = FloorMinute(closedBar.BucketTime, Minute);

            int existingIndex = CompletedBars.FindIndex(item => item.BucketTime == closedBar.BucketTime);
            if (existingIndex >= 0)
            {
                CompletedBars[existingIndex] = closedBar;
                RebuildFromCompletedBars();
            }
            else
            {
                AppendClosedBar(closedBar);
                TrimCompletedBars();
            }

            if (CurrentBar?.BucketTime == closedBar.BucketTime)
                CurrentBar = null;
        }

        public void ApplyRealtimeTick(long price, long tradeVolume, long tradeValue, DateTime tradeTime)
        {
            if (!IsReady || price <= 0)
                return;

            DateTime sourceTime = tradeTime == DateTime.MinValue ? DateTime.Now : tradeTime;
            DateTime bucketTime = FloorMinute(sourceTime, Minute);
            long safeVolume = Math.Max(0, tradeVolume);
            long safeTradingValue = tradeValue > 0
                ? tradeValue
                : (long)Math.Min(long.MaxValue, price * (double)safeVolume);

            if (CurrentBar != null && CurrentBar.BucketTime < bucketTime)
            {
                AppendClosedBar(CurrentBar);
                CurrentBar = null;
                TrimCompletedBars();
            }

            if (CurrentBar == null || CurrentBar.BucketTime != bucketTime)
            {
                CurrentBar = new StrategyMinuteBar
                {
                    BucketTime = bucketTime,
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    Volume = safeVolume,
                    TradingValue = safeTradingValue
                };
            }
            else
            {
                if (CurrentBar.Open <= 0)
                    CurrentBar.Open = price;
                CurrentBar.High = Math.Max(CurrentBar.High > 0 ? CurrentBar.High : price, price);
                CurrentBar.Low = CurrentBar.Low > 0 ? Math.Min(CurrentBar.Low, price) : price;
                CurrentBar.Close = price;
                CurrentBar.Volume += safeVolume;
                CurrentBar.TradingValue += safeTradingValue;
            }

            LastRealtimeAt = sourceTime;
        }

        public StrategyMinuteFrameSnapshot CreateSnapshot()
        {
            StrategyMinuteBar? completed = CompletedBars.LastOrDefault();
            return new StrategyMinuteFrameSnapshot
            {
                Code = Code,
                Market = Market,
                Minute = Minute,
                IsReady = IsReady,
                CompletedCount = CompletedBars.Count,
                TargetCount = TargetCount,
                LoadedAt = LoadedAt,
                LastRealtimeAt = LastRealtimeAt,

                CurrentBarTime = CurrentBar?.BucketTime ?? DateTime.MinValue,
                CurrentOpen = CurrentBar?.Open ?? 0,
                CurrentHigh = CurrentBar?.High ?? 0,
                CurrentLow = CurrentBar?.Low ?? 0,
                CurrentClose = CurrentBar?.Close ?? 0,
                CurrentVolume = CurrentBar?.Volume ?? 0,
                CurrentTradingValue = CurrentBar?.TradingValue ?? 0,

                LastCompletedBarTime = completed?.BucketTime ?? DateTime.MinValue,
                LastCompletedOpen = completed?.Open ?? 0,
                LastCompletedHigh = completed?.High ?? 0,
                LastCompletedLow = completed?.Low ?? 0,
                LastCompletedClose = completed?.Close ?? 0,
                LastCompletedVolume = completed?.Volume ?? 0,
                LastCompletedTradingValue = completed?.TradingValue ?? 0,

                Ma5 = AverageWithCurrent(_close5, 5),
                Ma10 = AverageWithCurrent(_close10, 10),
                Ma20 = AverageWithCurrent(_close20, 20),
                Ma60 = AverageWithCurrent(_close60, 60),
                Ma200 = AverageWithCurrent(_close200, 200),
                Ma240 = AverageWithCurrent(_close240, 240),
                Ma480 = AverageWithCurrent(_close480, 480),

                LastCompletedMa5 = completed?.Ma5 ?? 0,
                LastCompletedMa10 = completed?.Ma10 ?? 0,
                LastCompletedMa20 = completed?.Ma20 ?? 0,
                LastCompletedMa60 = completed?.Ma60 ?? 0,
                LastCompletedMa200 = completed?.Ma200 ?? 0,
                LastCompletedMa240 = completed?.Ma240 ?? 0,
                LastCompletedMa480 = completed?.Ma480 ?? 0,

                High20 = _high20.Max,
                Low20 = _low20.Min,
                HighestClose20 = _close20.Max,
                LowestClose20 = _close20.Min,
                Volume20 = _volume20.Sum,
                TradingValue20 = _tradingValue20.Sum
            };
        }

        public bool TryGetLastMa60TouchAnchor(DateTime baseDate, out StrategyMa60TouchAnchor anchor)
        {
            StrategyMinuteBar? touch = CompletedBars
                .Where(bar => bar.Ma60 > 0 &&
                    bar.BucketTime.Date == baseDate.Date &&
                    bar.Low <= bar.Ma60 &&
                    bar.High >= bar.Ma60)
                .OrderBy(bar => bar.BucketTime)
                .LastOrDefault();

            if (touch == null)
            {
                anchor = new StrategyMa60TouchAnchor();
                return false;
            }

            anchor = new StrategyMa60TouchAnchor
            {
                Minute = Minute,
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

        private void AppendClosedBar(StrategyMinuteBar bar)
        {
            bar.BucketTime = FloorMinute(bar.BucketTime, Minute);
            bar.Ma5 = _close5.AddAndAverage(bar.Close);
            bar.Ma10 = _close10.AddAndAverage(bar.Close);
            bar.Ma20 = _close20.AddAndAverage(bar.Close);
            bar.Ma60 = _close60.AddAndAverage(bar.Close);
            bar.Ma200 = _close200.AddAndAverage(bar.Close);
            bar.Ma240 = _close240.AddAndAverage(bar.Close);
            bar.Ma480 = _close480.AddAndAverage(bar.Close);
            _high20.Add(bar.High);
            _low20.Add(bar.Low);
            _volume20.Add(bar.Volume);
            _tradingValue20.Add(bar.TradingValue);
            CompletedBars.Add(bar);
        }

        private void RebuildFromCompletedBars()
        {
            CompletedBars = NormalizeBars(CompletedBars);
            ResetWindows();
            List<StrategyMinuteBar> source = [.. CompletedBars];
            CompletedBars = [];
            foreach (StrategyMinuteBar bar in source)
                AppendClosedBar(bar);
            TrimCompletedBars();
        }

        private void TrimCompletedBars()
        {
            int retain = Math.Max(TargetCount + 10, 500);
            if (CompletedBars.Count <= retain)
                return;

            CompletedBars = [.. CompletedBars.TakeLast(retain).Select(bar => bar.Clone())];
            RebuildFromCompletedBars();
        }

        private double AverageWithCurrent(RollingLongWindow completedWindow, int period)
        {
            StrategyMinuteBar? current = CurrentBar;
            if (current == null || current.Close <= 0)
                return completedWindow.Average;

            List<long> values = [.. completedWindow.Values.TakeLast(Math.Max(0, period - 1))];
            values.Add(current.Close);
            return values.Count >= period ? values.Average() : 0;
        }

        private void ResetWindows()
        {
            _close5.Clear();
            _close10.Clear();
            _close20.Clear();
            _close60.Clear();
            _close200.Clear();
            _close240.Clear();
            _close480.Clear();
            _high20.Clear();
            _low20.Clear();
            _volume20.Clear();
            _tradingValue20.Clear();
        }

        private static List<StrategyMinuteBar> NormalizeBars(IEnumerable<StrategyMinuteBar> bars)
        {
            return [.. (bars ?? [])
                .Where(bar => bar != null && bar.Close > 0 && bar.BucketTime != DateTime.MinValue)
                .GroupBy(bar => bar.BucketTime)
                .Select(group => group.Last().Clone())
                .OrderBy(bar => bar.BucketTime)];
        }

        private static DateTime FloorMinute(DateTime value, int minute)
        {
            int safeMinute = Math.Max(1, minute);
            int flooredMinute = value.Minute / safeMinute * safeMinute;
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, flooredMinute, 0);
        }

        private sealed class RollingLongWindow
        {
            private readonly Queue<long> _values = new();

            public RollingLongWindow(int capacity)
            {
                Capacity = Math.Max(1, capacity);
            }

            public int Capacity { get; }
            public long Sum { get; private set; }
            public IReadOnlyCollection<long> Values => _values;
            public double Average => _values.Count >= Capacity ? Sum / (double)Capacity : 0;
            public long Max => _values.Count == 0 ? 0 : _values.Max();
            public long Min => _values.Where(value => value > 0).DefaultIfEmpty(0).Min();

            public double AddAndAverage(long value)
            {
                Add(value);
                return Average;
            }

            public void Add(long value)
            {
                long safeValue = Math.Max(0, value);
                _values.Enqueue(safeValue);
                Sum += safeValue;
                while (_values.Count > Capacity)
                    Sum -= _values.Dequeue();
            }

            public void Clear()
            {
                _values.Clear();
                Sum = 0;
            }
        }
    }
}
