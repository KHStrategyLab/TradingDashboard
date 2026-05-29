namespace TradingDashboard.Models
{
    public sealed class WatchlistStockCacheEntry
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string MarketTypeCode { get; set; } = string.Empty;
        public string MarketName { get; set; } = string.Empty;
        public string ProgramMarketType { get; set; } = string.Empty;
        public long LastPrice { get; set; }
        public string OrderWarning { get; set; } = string.Empty;
        public string AuditInfo { get; set; } = string.Empty;
        public string StockState { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public bool SupportsNxt { get; set; }
        public long BasePrice { get; set; }
        public string BasePriceDate { get; set; } = string.Empty;
        public string BasePriceSource { get; set; } = string.Empty;
        public string SnapshotDate { get; set; } = string.Empty;
        public string LastSeenConditionDate { get; set; } = string.Empty;
        public DateTime LastUsedAt { get; set; } = DateTime.Now;
    }
}
