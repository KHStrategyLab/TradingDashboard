namespace TradingDashboard.Models
{
    public sealed class PaperTradeMarkEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string PositionKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SlotTag { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public long Price { get; set; }
        public long Amount { get; set; }
        public long EntryPrice { get; set; }
        public long ProfitLoss { get; set; }
        public decimal ProfitRate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Memo { get; set; } = string.Empty;
    }
}
