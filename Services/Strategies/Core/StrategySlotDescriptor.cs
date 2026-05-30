namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategySlotDescriptor(
        StrategySlotId Id,
        string Name,
        StrategyMarketScope MarketScope,
        string MarketBadgeText,
        string Summary,
        string Detail,
        string DocumentPath);
}
