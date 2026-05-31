using System.Text.Json.Serialization;

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
        public long CurrentPrice { get; set; }
        public long ChangeAmount { get; set; }
        public string ChangeRateText { get; set; } = string.Empty;
        public string VolumeText { get; set; } = string.Empty;
        public long LastPrice { get; set; }
        public string OrderWarning { get; set; } = string.Empty;
        public string AuditInfo { get; set; } = string.Empty;
        public string StockState { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public bool GateBaseCandleFound { get; set; }
        [JsonIgnore]
        public int GateBaseCandleOffset { get; set; } = -1;
        public string GateBaseCandleDate { get; set; } = string.Empty;
        public string GateBaseCandleMarket { get; set; } = string.Empty;
        public double GateBaseCandleChangeRate { get; set; }
        public long GateBaseCandleTradeValue { get; set; }
        public string GateBaseCandleCheckedDate { get; set; } = string.Empty;
        public bool SupportsNxt { get; set; }
        public long BasePrice { get; set; }
        public string BasePriceDate { get; set; } = string.Empty;
        public string BasePriceSource { get; set; } = string.Empty;
        public string SnapshotDate { get; set; } = string.Empty;
        public string LastSeenConditionDate { get; set; } = string.Empty;
        public DateTime LastUsedAt { get; set; } = DateTime.Now;
    }
}
