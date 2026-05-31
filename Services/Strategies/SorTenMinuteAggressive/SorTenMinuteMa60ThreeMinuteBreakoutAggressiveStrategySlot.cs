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
                "Aggressive SOR candidate. Uses the minute snapshot only: 10m MA60 recovery plus 3m 20-bar high breakout.",
                "Docs/Strategies/sor-ten-minute-ma60-three-minute-breakout-aggressive.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            StrategyMinuteBreakoutCheck minuteCheck = StrategyMinuteSignalChecks.EvaluateMa60Breakout(context, 10, 3);
            bool hasMinuteChart = minuteCheck.HasMinuteData;
            string minuteDataText = minuteCheck.FormatReadiness(10, 3);
            bool hasSignal = !context.IsOwned &&
                gatePassed &&
                hasBasePrice &&
                minuteCheck.HasSignal;

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : hasSignal ? "SIGNAL" : "WAIT",
                context.IsOwned ? "position tracking" : hasSignal ? "buy signal candidate" : "10min MA60 / 3min breakout tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "100B/20% gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("minute-data", minuteDataText, hasMinuteChart),
                    StrategyProgressCalculator.Step("ma60-pullback", "10m MA60 pullback", minuteCheck.Ma60Recovery),
                    StrategyProgressCalculator.Step("ma60-recovery", "10m MA60 recovery", minuteCheck.AboveMa60),
                    StrategyProgressCalculator.Step("breakout", "3m 20-high breakout", minuteCheck.BreakoutTriggered),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "10m MA60 stop", false),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: hasSignal ? 68 : gatePassed ? 40 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                hasSignal,
                context.IsOwned ? "TRACK" : hasSignal ? "SIGNAL" : "WAIT",
                context.IsOwned
                    ? $"exit tracking after aggressive entry / {minuteDataText}"
                    : minuteCheck.FormatSummary("10m MA60 / 3m breakout", 10, 3),
                progress);
        }
    }
}
