using TradingDashboard.Models;
using System.Collections.Generic;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyEvaluationContext
    {
        public WatchStockItem? Stock { get; init; }

        public StockStatusMetrics Metrics { get; init; } = new();

        public int ChartCandleCount { get; init; }

        public StrategyMinuteDataStatus MinuteData { get; init; } = new();

        public StrategyMinuteSnapshotSet? MinuteSnapshots { get; init; }

        public IReadOnlyDictionary<int, IReadOnlyList<StrategyMinuteBar>> MinuteBars { get; init; } =
            new Dictionary<int, IReadOnlyList<StrategyMinuteBar>>();

        public string Market { get; init; } = string.Empty;

        public bool IsOwned { get; init; }
    }
}
