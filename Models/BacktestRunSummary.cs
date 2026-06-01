namespace TradingDashboard.Models
{
    public sealed class BacktestRunSummary
    {
        public string RunId { get; set; } = string.Empty;
        public string StrategyCode { get; set; } = string.Empty;
        public string ExitRuleCode { get; set; } = string.Empty;
        public int SignalCount { get; set; }
        public int TradeCount { get; set; }
        public decimal WinRate { get; set; }
        public decimal AvgProfit { get; set; }
        public decimal AvgLoss { get; set; }
        public decimal Expectancy { get; set; }
        public long TotalProfit { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal MAE { get; set; }
        public decimal MFE { get; set; }
        public decimal AvgHoldingMinutes { get; set; }
        public int ConsecutiveLosses { get; set; }
        public long FeeAdjustedProfit { get; set; }
        public long SlippageAdjustedProfit { get; set; }
    }
}
