namespace TradingDashboard.Services.Strategies
{
    public sealed class ThreeMinutePullbackStrategySlot
        : TradingStrategySlotBase
    {
        public ThreeMinutePullbackStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.ThreeMinutePullback,
                "3min Pullback",
                StrategyMarketScope.Sor,
                "SOR",
                "Pullback, reaction, and confirmation after base candle",
                "10분 기준양봉 이후 바로 추격하지 않고, 3분봉에서 눌림 위치와 반응봉을 확인한 뒤 반응봉 고가 돌파를 후보로 본다. 시장은 선택된 실시간 시장을 따르되 기준가는 KRX 전일종가를 유지한다.",
                "Docs/Strategies/three-minute-pullback.md"))
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
                context.IsOwned ? "position tracking" : "pullback tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "base gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("chart", "3min data", hasChart),
                    StrategyProgressCalculator.Step("pullback", "pullback zone", false),
                    StrategyProgressCalculator.Step("reaction", "reaction candle", false),
                    StrategyProgressCalculator.Step("breakout", "reaction high", false),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "pullback stop", false),
                    StrategyProgressCalculator.Step("target1", "base high", false),
                    StrategyProgressCalculator.Step("target2", "extension", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: gatePassed ? 38 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                context.IsOwned ? "TRACK" : "WAIT",
                context.IsOwned ? "exit tracking after pullback buy" : "pullback and reaction checks pending",
                progress);
        }
    }
}
