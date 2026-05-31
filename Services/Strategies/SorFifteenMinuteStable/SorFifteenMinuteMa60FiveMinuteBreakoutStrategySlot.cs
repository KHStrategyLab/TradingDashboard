namespace TradingDashboard.Services.Strategies
{
    public sealed class SorFifteenMinuteMa60FiveMinuteBreakoutStrategySlot
        : TradingStrategySlotBase
    {
        public SorFifteenMinuteMa60FiveMinuteBreakoutStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.ThreeMinutePullback,
                "SOR 15min MA60 + 5min Breakout",
                StrategyMarketScope.Sor,
                "SOR",
                "15min MA60 pullback + 5min 20-bar breakout",
                "SOR ON 운용을 전제로, 시가가 일봉 60일선 근처에 있고 당일 20% 이상 상승/거래대금 1,000억 이상인 후보에서 15분봉 MA60 눌림을 확인한 뒤 5분봉 20봉 신고가 돌파를 주전략 후보로 본다. 현재 단계에서는 실주문보다 검증실행/알림 중심으로 운용한다.",
                "Docs/Strategies/sor-fifteen-minute-ma60-five-minute-breakout.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            bool hasMinuteChart = context.MinuteData.HasAll(15, 5);
            string minuteDataText = context.MinuteData.Format(15, 5);

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : "WAIT",
                context.IsOwned ? "position tracking" : "15min MA60 / 5min breakout tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "100B/20% gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("minute-data", minuteDataText, hasMinuteChart),
                    StrategyProgressCalculator.Step("daily-ma60", "daily MA60 open zone", false),
                    StrategyProgressCalculator.Step("ma60-pullback", "15m MA60 pullback", false),
                    StrategyProgressCalculator.Step("breakout", "5m 20-high breakout", false),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "15m MA60 stop", false),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: gatePassed ? 42 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                context.IsOwned ? "TRACK" : "WAIT",
                context.IsOwned
                    ? $"exit tracking after stable breakout entry · {minuteDataText}"
                    : $"15min MA60 pullback and 5min breakout checks pending · {minuteDataText}",
                progress);
        }
    }
}
