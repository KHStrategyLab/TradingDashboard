using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class StockMasterCacheDocument
    {
        public StockMasterCacheMeta Meta { get; set; } = new();
        public List<StockMasterItem> Items { get; set; } = [];
    }

    public sealed class StockMasterCacheMeta
    {
        public string UpdatedAt { get; set; } = string.Empty;
        public string TradingDate { get; set; } = string.Empty;
        public string Source { get; set; } = "ka10099";
        public string OrderMarketPolicy { get; set; } = "SOR";
        public string BasePriceMarketPolicy { get; set; } = "KRX";
    }

    public sealed class StockMasterItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MarketCode { get; set; } = string.Empty;
        public string MarketName { get; set; } = string.Empty;
        public string ProgramMarketType { get; set; } = string.Empty;
        public bool SupportsNxt { get; set; }
        public string LastPrice { get; set; } = string.Empty;
        public string OrderWarning { get; set; } = string.Empty;
        public string AuditInfo { get; set; } = string.Empty;
        public string StockState { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public string CompanyClassName { get; set; } = string.Empty;
    }
}
