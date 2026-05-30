namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategySlotSetting(
        StrategySlotId Id,
        string Name,
        bool IsEnabled);
}
