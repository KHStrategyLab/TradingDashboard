namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyProgressStep(
        string Key,
        string Label,
        bool IsCompleted,
        bool IsCurrent);
}
