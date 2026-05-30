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

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context) =>
            StrategyEvaluationResult.Waiting(Id, Name, "pullback -, reaction -, confirmation -");
    }
}
