namespace TradingDashboard.Services.Strategies
{
    public abstract class TradingStrategySlotBase : ITradingStrategySlot
    {
        protected TradingStrategySlotBase(StrategySlotDescriptor descriptor)
        {
            Descriptor = descriptor;
            Id = descriptor.Id;
            Name = descriptor.Name;
        }

        public StrategySlotId Id { get; }

        public string Name { get; }

        public StrategySlotDescriptor Descriptor { get; }

        public abstract StrategyEvaluationResult Evaluate(StrategyEvaluationContext context);
    }
}
