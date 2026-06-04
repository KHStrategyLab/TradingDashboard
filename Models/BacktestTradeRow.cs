namespace TradingDashboard.Models
{
    public sealed class BacktestTradeRow
    {
        public string RunId { get; set; } = string.Empty;
        public string RunMode { get; set; } = string.Empty;
        public string MarketMode { get; set; } = string.Empty;
        public string StrategyCode { get; set; } = string.Empty;
        public string ExitRuleCode { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string Status { get; set; } = "Completed";
        public string EntryTime { get; set; } = string.Empty;
        public string ExitTime { get; set; } = string.Empty;
        public long EntryPrice { get; set; }
        public long ExitPrice { get; set; }
        public long MaxHigh { get; set; }
        public long MinLow { get; set; }
        public long StopPrice { get; set; }
        public int Quantity { get; set; }
        public decimal ProfitRate { get; set; }
        public long ProfitAmount { get; set; }
        public decimal Mae { get; set; }
        public decimal Mfe { get; set; }
        public decimal RiskRate { get; set; }
        public decimal MaxR { get; set; }
        public decimal MinR { get; set; }
        public int HoldingMinutes { get; set; }
        public string EntryReason { get; set; } = string.Empty;
        public string ExitReason { get; set; } = string.Empty;
    }
}
