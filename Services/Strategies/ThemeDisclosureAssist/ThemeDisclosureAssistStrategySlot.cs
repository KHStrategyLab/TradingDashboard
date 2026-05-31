namespace TradingDashboard.Services.Strategies
{
    public sealed class ThemeDisclosureAssistStrategySlot
        : TradingStrategySlotBase
    {
        public ThemeDisclosureAssistStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.ThemeDisclosureAssist,
                "Manual Buy Stop Assist",
                StrategyMarketScope.Assist,
                "ASSIST",
                "Manual holdings stop-loss only",
                "No buy signal. Watches holdings without an automatic strategy tag and raises a stop signal from the entry 5-minute low or a 5-minute MA5 breakdown.",
                "Docs/Strategies/theme-disclosure-assist.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool isOwned = context.IsOwned;
            bool hasFiveMinuteData = context.MinuteSnapshots?.Get(5)?.Ma5 > 0;

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                "MANUAL_STOP",
                "manual holding stop assist",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("manual-holding", "manual holding", isOwned),
                    StrategyProgressCalculator.Step("5m-data", "5m stop data", hasFiveMinuteData),
                    StrategyProgressCalculator.Step("entry-low", "entry 5m low anchor", false),
                    StrategyProgressCalculator.Step("stop-signal", "stop signal", false)
                ],
                isOwned: false,
                strengthPercent: isOwned && hasFiveMinuteData ? 35 : hasStock ? 10 : 0,
                strengthLabel: "stop assist");

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                "MANUAL_STOP",
                "manual holdings only; no buy signal",
                progress);
        }
    }
}
