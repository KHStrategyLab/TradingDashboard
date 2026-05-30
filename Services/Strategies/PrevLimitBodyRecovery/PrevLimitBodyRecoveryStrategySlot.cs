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

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context) =>
            StrategyEvaluationResult.Waiting(Id, Name, "KRX base candle import pending");
    }
}
