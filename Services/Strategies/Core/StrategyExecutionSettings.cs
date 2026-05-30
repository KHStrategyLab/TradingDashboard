namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyExecutionSettings(
        bool AutoTradingEnabled,
        bool LiveBuyEnabled,
        long Budget,
        int SlotCount)
    {
        public bool AllowsLiveBuy => AutoTradingEnabled && LiveBuyEnabled;

        public bool BlocksLiveOrders => !AllowsLiveBuy;

        public string ExecutionModeText =>
            AllowsLiveBuy
                ? "LIVE_ORDERS"
                : AutoTradingEnabled ? "ENGINE_ONLY" : "OFF";
    }
}
