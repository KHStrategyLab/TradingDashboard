namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyEvaluationResult(
        StrategySlotId SlotId,
        string Name,
        bool HasSignal,
        string StateText,
        string Summary,
        StrategyProgressSnapshot? Progress = null)
    {
        public static StrategyEvaluationResult Ignored(StrategySlotId slotId, string name) =>
            new(slotId, name, false, "미적용", "slot disabled", StrategyProgressSnapshot.Empty(slotId));

        public static StrategyEvaluationResult Waiting(StrategySlotId slotId, string name, string summary) =>
            new(slotId, name, false, "WAIT", summary, StrategyProgressSnapshot.Empty(slotId));
    }
}
