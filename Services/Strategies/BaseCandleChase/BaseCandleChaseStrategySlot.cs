namespace TradingDashboard.Services.Strategies
{
    public sealed class BaseCandleChaseStrategySlot
        : TradingStrategySlotBase
    {
        public BaseCandleChaseStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.BaseCandleChase,
                "10min Base Candle Chase",
                StrategyMarketScope.Sor,
                "SOR",
                "10min value/volume burst + high breakout",
                "10분봉에서 거래량과 거래대금이 터진 기준양봉 후보를 찾고, 고가 돌파가 과열 구간을 넘지 않을 때만 추격 후보로 본다. KRX/NXT 현재 시장 데이터는 사용할 수 있지만 기준가는 항상 KRX 전일종가로 고정한다.",
                "Docs/Strategies/base-candle-chase.md"))
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
                context.IsOwned ? "position tracking" : "candidate tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "base gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("chart", "10min data", hasChart),
                    StrategyProgressCalculator.Step("base-candle", "10min burst", false),
                    StrategyProgressCalculator.Step("breakout", "high breakout", false),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "stop guard", false),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: gatePassed ? 42 : hasStock ? 14 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                false,
                context.IsOwned ? "TRACK" : "WAIT",
                context.IsOwned ? "exit tracking after buy" : "base candle and breakout checks pending",
                progress);
        }
    }
}
