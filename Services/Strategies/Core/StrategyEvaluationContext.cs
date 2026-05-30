using TradingDashboard.Models;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyEvaluationContext
    {
        public WatchStockItem? Stock { get; init; }

        public StockStatusMetrics Metrics { get; init; } = new();

        public int ChartCandleCount { get; init; }

        public string Market { get; init; } = string.Empty;
    }
}
