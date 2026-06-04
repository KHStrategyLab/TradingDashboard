namespace TradingDashboard.Models
{
    public sealed class BacktestSignalRow
    {
        public string RunId { get; set; } = string.Empty;
        public string RunMode { get; set; } = string.Empty;
        public string MarketMode { get; set; } = string.Empty;
        public string StrategyCode { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Market { get; set; } = "UNKNOWN";
        public string Status { get; set; } = "Generated";
        public string SignalTime { get; set; } = string.Empty;
        public string SignalType { get; set; } = string.Empty;
        public long Price { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
