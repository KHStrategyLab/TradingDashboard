namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyExecutionSettings(
        bool AutoTradingEnabled,
        bool PaperTradingMode,
        long Budget,
        int SlotCount)
    {
        public bool BlocksLiveOrders => PaperTradingMode;

        public string ExecutionModeText =>
            PaperTradingMode
                ? "EXPERIMENT"
                : AutoTradingEnabled ? "LIVE" : "OFF";
    }
}
