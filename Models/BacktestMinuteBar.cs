namespace TradingDashboard.Models
{
    public sealed class BacktestMinuteBar
    {
        public string Code { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public int Minute { get; set; }
        public string DateTime { get; set; } = string.Empty;
        public long Open { get; set; }
        public long High { get; set; }
        public long Low { get; set; }
        public long Close { get; set; }
        public long Volume { get; set; }
        public long TradingValue { get; set; }
        public string Status { get; set; } = "Downloaded";
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
