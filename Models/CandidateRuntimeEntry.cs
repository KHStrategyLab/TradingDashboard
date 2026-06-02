namespace TradingDashboard.Models
{
    public sealed class CandidateRuntimeEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string CandidateDate { get; set; } = string.Empty;
        public string CandidateTime { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ConditionName { get; set; } = string.Empty;
        public string ConditionId { get; set; } = string.Empty;
        public long CurrentPrice { get; set; }
        public long ChangeAmount { get; set; }
        public string ChangeRateText { get; set; } = "-";
        public string VolumeText { get; set; } = "-";
        public long TradingValue { get; set; }
        public bool NxtEnabled { get; set; }
        public string MarketTypeCode { get; set; } = string.Empty;
        public string MarketName { get; set; } = string.Empty;
        public string ProgramMarketType { get; set; } = string.Empty;
        public string OrderWarning { get; set; } = string.Empty;
        public string AuditInfo { get; set; } = string.Empty;
        public string StockState { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
