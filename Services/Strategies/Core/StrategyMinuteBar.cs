using System;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteBar
    {
        public DateTime BucketTime { get; set; }
        public long Open { get; set; }
        public long High { get; set; }
        public long Low { get; set; }
        public long Close { get; set; }
        public long Volume { get; set; }
        public long TradingValue { get; set; }
        public double Ma5 { get; set; }
        public double Ma10 { get; set; }
        public double Ma20 { get; set; }
        public double Ma60 { get; set; }
        public double Ma200 { get; set; }
        public double Ma240 { get; set; }
        public double Ma480 { get; set; }

        public StrategyMinuteBar Clone() => new()
        {
            BucketTime = BucketTime,
            Open = Open,
            High = High,
            Low = Low,
            Close = Close,
            Volume = Volume,
            TradingValue = TradingValue,
            Ma5 = Ma5,
            Ma10 = Ma10,
            Ma20 = Ma20,
            Ma60 = Ma60,
            Ma200 = Ma200,
            Ma240 = Ma240,
            Ma480 = Ma480
        };
    }
}
