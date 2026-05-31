namespace TradingDashboard.Models
{
    public sealed class StrategyOrderJournalEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SlotId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public long ReferencePrice { get; set; }
        public long OrderPrice { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public int ReturnCode { get; set; }
        public string ReturnMessage { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string SavedAt { get; set; } = string.Empty;
        public string Memo { get; set; } = string.Empty;
    }
}
