namespace TradingDashboard.Services.Strategies
{
    public sealed class ThemeDisclosureAssistStrategySlot
        : TradingStrategySlotBase
    {
        public ThemeDisclosureAssistStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.ThemeDisclosureAssist,
                "Theme / Disclosure Assist",
                StrategyMarketScope.Assist,
                "보조",
                "Theme, disclosure, and news tags only",
                "공시, 뉴스, 테마 태그를 이용해 후보의 우선순위를 보조한다. 이 슬롯은 단독 매수신호를 만들지 않고, 좋은 재료 확인이나 위험 태그 표시만 담당한다.",
                "Docs/Strategies/theme-disclosure-assist.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                "ASSIST",
                "assist tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "base gate", gatePassed),
                    StrategyProgressCalculator.Step("news", "news tags", false),
                    StrategyProgressCalculator.Step("disclosure", "filing tags", false),
                    StrategyProgressCalculator.Step("theme", "theme link", false)
                ],
                isOwned: false,
                strengthPercent: gatePassed ? 28 : hasStock ? 14 : 0,
                strengthLabel: "보조강도");

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                "ASSIST",
                "theme, disclosure, and news tag checks pending",
                progress);
        }
    }
}
