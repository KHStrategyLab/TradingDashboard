namespace TradingDashboard.Services.Strategies
{
    public sealed class SorTenMinuteMa60FiveMinuteBreakoutStrategySlot
        : TradingStrategySlotBase
    {
        public SorTenMinuteMa60FiveMinuteBreakoutStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.SorTenMinuteFiveMinuteBreakout,
                "SOR 10min MA60 + 5min Breakout",
                StrategyMarketScope.Sor,
                "SOR",
                "10min MA60 pullback + 5min 20-bar breakout",
                "SOR ON 운용을 전제로, 당일 20% 이상 상승/거래대금 1,000억 이상인 후보에서 10분봉 MA60 눌림과 회복을 확인한 뒤 5분봉 20봉 신고가 돌파를 중간형 후보로 본다. 3분 공격형보다 느리고 15분 안정형보다 빠른 비교 검증용 전략이다.",
                "Docs/Strategies/sor-ten-minute-ma60-five-minute-breakout-middle.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            bool hasMinuteChart = context.MinuteData.HasAll(10, 5);
            string minuteDataText = context.MinuteData.Format(10, 5);

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : "WAIT",
                context.IsOwned ? "position tracking" : "10min MA60 / 5min breakout tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "100B/20% gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("minute-data", minuteDataText, hasMinuteChart),
                    StrategyProgressCalculator.Step("ma60-pullback", "10m MA60 pullback", false),
                    StrategyProgressCalculator.Step("ma60-recovery", "10m MA60 recovery", false),
                    StrategyProgressCalculator.Step("breakout", "5m 20-high breakout", false),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "10m MA60 stop", false),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: gatePassed ? 41 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                context.IsOwned ? "TRACK" : "WAIT",
                context.IsOwned
                    ? $"exit tracking after middle breakout entry · {minuteDataText}"
                    : $"10min MA60 pullback and 5min breakout checks pending · {minuteDataText}",
                progress);
        }
    }
}
