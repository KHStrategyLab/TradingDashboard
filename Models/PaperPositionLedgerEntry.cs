namespace TradingDashboard.Models
{
    public sealed class PaperPositionLedgerEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SlotTag { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public long EntryPrice { get; set; }
        public long CurrentPrice { get; set; }
        public long ProfitLoss { get; set; }
        public decimal ProfitRate { get; set; }
        public string EntryTime { get; set; } = string.Empty;
        public string ExitTime { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
