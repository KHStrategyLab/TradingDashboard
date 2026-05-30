namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyDuplicatePolicy(
        bool AllowAdditionalBuy,
        bool NotifyDuplicateSignal);
}
