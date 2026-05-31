namespace TradingDashboard.Models
{
    public sealed class ManualPositionLedgerEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public long OpenQuantity { get; set; }
        public long AveragePrice { get; set; }
        public long Entry5MinuteLow { get; set; }
        public string FillTime { get; set; } = string.Empty;
        public string Entry5MinuteTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public string Memo { get; set; } = string.Empty;
    }
}
