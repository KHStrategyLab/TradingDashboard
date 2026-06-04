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
                "Middle SOR candidate. Uses the minute snapshot only: 10m MA60 recovery plus 5m 20-bar high breakout.",
                "Docs/Strategies/sor-ten-minute-ma60-five-minute-breakout-middle.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            StrategyMinuteBreakoutCheck minuteCheck = StrategyMinuteSignalChecks.EvaluateMa60Breakout(context, 10, 5);
            StrategyExitFirstPlan exitPlan = StrategyExitFirstPlanner.FromMa60Breakout(minuteCheck, maxStopRiskPercent: 3.0);
            bool hasMinuteChart = minuteCheck.HasMinuteData;
            string minuteDataText = minuteCheck.FormatReadiness(10, 5);
            bool setupSignal = !context.IsOwned &&
                gatePassed &&
                hasBasePrice &&
                minuteCheck.HasSignal;
            bool hasSignal = setupSignal && exitPlan.IsTradable;
            StrategyOrderIntent orderIntent = hasSignal
                ? StrategyOrderIntent.BuyNow(
                    "10m MA60 recovery + 5m 20-high breakout",
                    minuteCheck.SignalPrice,
                    exitPlan.StopPrice,
                    exitPlan.TargetPrice,
                    exitPlan.RewardRiskRatio)
                : StrategyOrderIntent.Watch(
                    setupSignal ? "exit-first blocked before order handoff" : "waiting for 10m MA60 recovery and 5m breakout",
                    exitPlan.NoBuyReasons);

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : hasSignal ? "SIGNAL" : "WAIT",
                context.IsOwned ? "position tracking" : hasSignal ? "buy signal candidate" : "10min MA60 / 5min breakout tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "70B/25% or 300B/20 gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("minute-data", minuteDataText, hasMinuteChart),
                    StrategyProgressCalculator.Step("ma60-pullback", "10m MA60 pullback", minuteCheck.Ma60Recovery),
                    StrategyProgressCalculator.Step("ma60-recovery", "10m MA60 recovery", minuteCheck.AboveMa60),
                    StrategyProgressCalculator.Step("breakout", "5m 20-high breakout", minuteCheck.BreakoutTriggered),
                    StrategyProgressCalculator.Step("exit-first", "exit-first RR", exitPlan.IsTradable),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "10m MA60 stop", false),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: hasSignal ? 66 : gatePassed ? 41 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                hasSignal,
                context.IsOwned ? "TRACK" : hasSignal ? "SIGNAL" : "WAIT",
                context.IsOwned
                    ? $"exit tracking after middle breakout entry / {minuteDataText}"
                    : $"{minuteCheck.FormatSummary("10m MA60 / 5m breakout", 10, 5)} / {StrategyExitFirstPlanner.FormatSummary(exitPlan)}",
                progress,
                orderIntent);
        }
    }
}
