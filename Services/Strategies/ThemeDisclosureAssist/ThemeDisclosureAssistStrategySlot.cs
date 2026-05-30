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

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context) =>
            StrategyEvaluationResult.Waiting(Id, Name, "theme -, disclosure -, news -");
    }
}
