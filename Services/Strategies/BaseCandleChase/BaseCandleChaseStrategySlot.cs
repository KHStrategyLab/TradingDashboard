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

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context) =>
            StrategyEvaluationResult.Waiting(Id, Name, "base candle -, breakout -, stop -, target -");
    }
}
