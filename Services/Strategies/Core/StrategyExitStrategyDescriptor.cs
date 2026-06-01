namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyExitStrategyDescriptor(
        string Code,
        string Name,
        string Summary,
        bool IsManualOnly = false);
}
