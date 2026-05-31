using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class ChartCandleCacheDocument
    {
        public ChartCandleCacheMeta Meta { get; set; } = new();
        public List<ChartCandleCacheEntry> Items { get; set; } = [];
    }

    public sealed class ChartCandleCacheMeta
    {
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public sealed class ChartCandleCacheEntry
    {
        public string Code { get; set; } = string.Empty;
        public bool UseNxtMarket { get; set; }
        public string Period { get; set; } = string.Empty;
        public string CachedAt { get; set; } = string.Empty;
        public List<DailyCandle> Candles { get; set; } = [];
    }
}
