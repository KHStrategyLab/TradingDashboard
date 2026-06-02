namespace TradingDashboard.Models
{
    public sealed class LeaderHistoryEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string BaseDate { get; set; } = string.Empty;
        public int PriorityRank { get; set; }
        public int DailyRank { get; set; }
        public long Open { get; set; }
        public long High { get; set; }
        public long Low { get; set; }
        public long Close { get; set; }
        public long Volume { get; set; }
        public long TradingValue { get; set; }
        public decimal ChangeRate { get; set; }
        public decimal CloseLocationPercent { get; set; }
        public decimal UpperTailPercent { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? ValueToMarketCapPercent { get; set; }
        public decimal? TurnoverRate { get; set; }
        public long KrxClose { get; set; }
        public long NxtClose { get; set; }
        public bool BollingerUpperBreak { get; set; }
        public bool PrevHighPlus10 { get; set; }
        public decimal QualityScore { get; set; }
        public string QualityGrade { get; set; } = string.Empty;
        public string LeaderType { get; set; } = string.Empty;
        public string QualityReason { get; set; } = string.Empty;
        public string Source { get; set; } = "Rebuild";
        public string Status { get; set; } = "Active";
        public string SavedAt { get; set; } = string.Empty;
        public int ExpiresAfterTradingDays { get; set; } = 6;
        public bool ManualDiscarded { get; set; }
    }
}
