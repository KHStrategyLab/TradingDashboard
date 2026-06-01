namespace TradingDashboard.Models
{
    public sealed class BacktestBaseCandle
    {
        public string Key { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string BaseCandleDate { get; set; } = string.Empty;
        public long BaseOpen { get; set; }
        public long BaseHigh { get; set; }
        public long BaseLow { get; set; }
        public long BaseClose { get; set; }
        public long BaseVolume { get; set; }
        public long BaseTradingValue { get; set; }
        public long PreviousClose { get; set; }
        public decimal ChangeRate { get; set; }
        public decimal CloseLocationPercent { get; set; }
        public decimal UpperTailPercent { get; set; }
        public string Status { get; set; } = "Verified";
        public string SourceName { get; set; } = string.Empty;
        public string VerifiedAt { get; set; } = string.Empty;
    }
}
