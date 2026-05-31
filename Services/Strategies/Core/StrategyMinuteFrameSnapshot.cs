using System;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteFrameSnapshot
    {
        public string Code { get; init; } = string.Empty;
        public string Market { get; init; } = string.Empty;
        public int Minute { get; init; }
        public bool IsReady { get; init; }
        public int CompletedCount { get; init; }
        public int TargetCount { get; init; }
        public DateTime LoadedAt { get; init; }
        public DateTime LastRealtimeAt { get; init; }

        public DateTime CurrentBarTime { get; init; }
        public long CurrentOpen { get; init; }
        public long CurrentHigh { get; init; }
        public long CurrentLow { get; init; }
        public long CurrentClose { get; init; }
        public long CurrentVolume { get; init; }
        public long CurrentTradingValue { get; init; }

        public DateTime LastCompletedBarTime { get; init; }
        public long LastCompletedOpen { get; init; }
        public long LastCompletedHigh { get; init; }
        public long LastCompletedLow { get; init; }
        public long LastCompletedClose { get; init; }
        public long LastCompletedVolume { get; init; }
        public long LastCompletedTradingValue { get; init; }

        public double Ma5 { get; init; }
        public double Ma10 { get; init; }
        public double Ma20 { get; init; }
        public double Ma60 { get; init; }
        public double Ma200 { get; init; }
        public double Ma240 { get; init; }
        public double Ma480 { get; init; }

        public double LastCompletedMa5 { get; init; }
        public double LastCompletedMa10 { get; init; }
        public double LastCompletedMa20 { get; init; }
        public double LastCompletedMa60 { get; init; }
        public double LastCompletedMa200 { get; init; }
        public double LastCompletedMa240 { get; init; }
        public double LastCompletedMa480 { get; init; }

        public long High20 { get; init; }
        public long Low20 { get; init; }
        public long HighestClose20 { get; init; }
        public long LowestClose20 { get; init; }
        public long Volume20 { get; init; }
        public long TradingValue20 { get; init; }

        public bool HasMa60 => Ma60 > 0 || LastCompletedMa60 > 0;

        public bool HasBreakout20 => High20 > 0 && HighestClose20 > 0;
    }
}
