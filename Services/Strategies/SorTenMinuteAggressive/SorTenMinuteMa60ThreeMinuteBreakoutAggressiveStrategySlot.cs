namespace TradingDashboard.Services.Strategies
{
    public sealed class SorTenMinuteMa60ThreeMinuteBreakoutAggressiveStrategySlot
        : TradingStrategySlotBase
    {
        public SorTenMinuteMa60ThreeMinuteBreakoutAggressiveStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.BaseCandleChase,
                "SOR 10min MA60 + 3min Breakout",
                StrategyMarketScope.Sor,
                "SOR",
                "10min MA60 pullback + 3min 20-bar breakout",
                "SOR ON 운용을 전제로, 당일 강세 후보에서 10분봉 MA60 눌림과 회복을 확인한 뒤 3분봉 20봉 신고가 돌파를 공격형 후보로 본다. 빠른 대신 속임수 돌파가 많으므로 현재 단계에서는 실주문보다 검증실행/알림 중심으로 운용한다.",
                "Docs/Strategies/sor-ten-minute-ma60-three-minute-breakout-aggressive.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            bool hasMinuteChart = context.MinuteData.HasAll(10, 3);
            string minuteDataText = context.MinuteData.Format(10, 3);

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : "WAIT",
                context.IsOwned ? "position tracking" : "10min MA60 / 3min breakout tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "100B/20% gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("minute-data", minuteDataText, hasMinuteChart),
                    StrategyProgressCalculator.Step("ma60-pullback", "10m MA60 pullback", false),
                    StrategyProgressCalculator.Step("ma60-recovery", "10m MA60 recovery", false),
                    StrategyProgressCalculator.Step("breakout", "3m 20-high breakout", false),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "10m MA60 stop", false),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: gatePassed ? 40 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                context.IsOwned ? "TRACK" : "WAIT",
                context.IsOwned
                    ? $"exit tracking after aggressive entry · {minuteDataText}"
                    : $"10min MA60 pullback and 3min breakout checks pending · {minuteDataText}",
                progress);
        }
    }
}
