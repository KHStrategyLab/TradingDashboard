namespace TradingDashboard.Services.Strategies
{
    public sealed class PrevLimitBodyRecoveryStrategySlot
        : TradingStrategySlotBase
    {
        public PrevLimitBodyRecoveryStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.PrevLimitBodyRecovery,
                "Prev Limit Body Recovery",
                StrategyMarketScope.KrxOnly,
                "KRX",
                "KRX previous limit candle body pullback and open recovery",
                "전일 상한가급 KRX 기준봉의 몸통 안으로 눌린 뒤, 저점 갱신 중단과 회복봉을 확인하고 탈출봉 고가 돌파를 후보로 본다. 기준봉, 전일종가, 몸통 범위는 KRX 정규장 일봉으로 고정한다.",
                "Docs/Strategies/prev-limit-body-recovery.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            bool hasChart = context.ChartCandleCount > 0;

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : "WAIT",
                context.IsOwned ? "position tracking" : "KRX recovery tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "KRX gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("daily-chart", "daily data", hasChart),
                    StrategyProgressCalculator.Step("body-pullback", "body pullback", false),
                    StrategyProgressCalculator.Step("recovery", "open recovery", false),
                    StrategyProgressCalculator.Step("breakout", "escape high", false),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "body stop", false),
                    StrategyProgressCalculator.Step("target", "target", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: gatePassed ? 40 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                context.IsOwned ? "TRACK" : "WAIT",
                context.IsOwned ? "exit tracking after recovery buy" : "KRX body recovery checks pending",
                progress);
        }
    }
}
