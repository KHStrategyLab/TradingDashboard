using System.Collections.Generic;

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
                "Stable SOR candidate. Uses the minute snapshot only: 15m MA60 recovery plus 5m 20-bar high breakout.",
                "Docs/Strategies/sor-fifteen-minute-ma60-five-minute-breakout.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool gatePassed = context.Stock?.GateBaseCandleFound == true;
            bool hasBasePrice = context.Stock?.LastPrice > 0 || context.Metrics.BasePriceText != "-";
            StrategyMinuteBreakoutCheck minuteCheck = StrategyMinuteSignalChecks.EvaluateMa60Breakout(context, 15, 5);
            StrategyExitFirstPlan exitPlan = StrategyExitFirstPlanner.FromMa60Breakout(minuteCheck, maxStopRiskPercent: 3.0);
            IReadOnlyList<StrategyMinuteBar> exitBars = context.MinuteBars.TryGetValue(15, out IReadOnlyList<StrategyMinuteBar>? fifteenBars) ? fifteenBars : [];
            StrategyExitBreakEvaluation exitBreak = StrategyExitBreakEvaluator.Evaluate(exitBars, exitPlan.StopPrice);
            bool hasMinuteChart = minuteCheck.HasMinuteData;
            string minuteDataText = minuteCheck.FormatReadiness(15, 5);
            bool setupSignal = !context.IsOwned &&
                gatePassed &&
                hasBasePrice &&
                minuteCheck.HasSignal;
            bool hasSignal = setupSignal && exitPlan.IsTradable;
            StrategyOrderIntent orderIntent = hasSignal
                ? StrategyOrderIntent.BuyNow(
                    "15m MA60 recovery + 5m 20-high breakout",
                    minuteCheck.SignalPrice,
                    exitPlan.StopPrice,
                    exitPlan.TargetPrice,
                    exitPlan.RewardRiskRatio)
                : StrategyOrderIntent.Watch(
                    setupSignal ? "exit-first blocked before order handoff" : "waiting for 15m MA60 recovery and 5m breakout",
                    exitPlan.NoBuyReasons);

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned && exitBreak.ShouldExit ? "EXIT" : context.IsOwned ? "OWNED" : hasSignal ? "SIGNAL" : "WAIT",
                context.IsOwned ? $"position tracking / {exitBreak.Reason}" : hasSignal ? "buy signal candidate" : "15min MA60 / 5min breakout tracking",
                [
                    StrategyProgressCalculator.Step("condition", "condition", hasStock),
                    StrategyProgressCalculator.Step("gate", "70B/25% or 300B/20 gate", gatePassed),
                    StrategyProgressCalculator.Step("base-price", "KRX base", hasBasePrice),
                    StrategyProgressCalculator.Step("minute-data", minuteDataText, hasMinuteChart),
                    StrategyProgressCalculator.Step("daily-ma60", "daily MA60 open zone", false),
                    StrategyProgressCalculator.Step("ma60-pullback", "15m MA60 pullback", minuteCheck.Ma60Recovery),
                    StrategyProgressCalculator.Step("breakout", "5m 20-high breakout", minuteCheck.BreakoutTriggered),
                    StrategyProgressCalculator.Step("exit-first", "exit-first RR", exitPlan.IsTradable),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", StrategyExitBreakEvaluator.ToProgressLabel(exitBreak), context.IsOwned && exitBreak.ShouldExit),
                    StrategyProgressCalculator.Step("target1", "target 1", false),
                    StrategyProgressCalculator.Step("trail", "trail", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                strengthPercent: hasSignal ? 64 : gatePassed ? 42 : hasStock ? 12 : 0);

            return new StrategyEvaluationResult(
                Id,
                Name,
                hasSignal,
                context.IsOwned && exitBreak.ShouldExit ? "EXIT" : context.IsOwned ? "TRACK" : hasSignal ? "SIGNAL" : "WAIT",
                context.IsOwned
                    ? $"exit tracking after stable breakout entry / {minuteDataText} / {exitBreak.Reason}"
                    : $"{minuteCheck.FormatSummary("15m MA60 / 5m breakout", 15, 5)} / {StrategyExitFirstPlanner.FormatSummary(exitPlan)}",
                progress,
                orderIntent);
        }
    }
}
