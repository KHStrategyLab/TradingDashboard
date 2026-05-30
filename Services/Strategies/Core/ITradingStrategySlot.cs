namespace TradingDashboard.Services.Strategies
{
    public interface ITradingStrategySlot
    {
        StrategySlotId Id { get; }

        string Name { get; }

        StrategySlotDescriptor Descriptor { get; }

        StrategyEvaluationResult Evaluate(StrategyEvaluationContext context);
    }
}
