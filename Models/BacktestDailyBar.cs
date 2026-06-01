namespace TradingDashboard.Models
{
    public sealed class BacktestDailyBar
    {
        public string Code { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string Date { get; set; } = string.Empty;
        public long Open { get; set; }
        public long High { get; set; }
        public long Low { get; set; }
        public long Close { get; set; }
        public long Volume { get; set; }
        public long TradingValue { get; set; }
        public long PreviousClose { get; set; }
        public decimal ChangeRate { get; set; }
        public decimal CloseLocationPercent { get; set; }
        public string Status { get; set; } = "Downloaded";
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
