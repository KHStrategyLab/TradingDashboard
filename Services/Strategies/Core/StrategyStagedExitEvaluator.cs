using System;
using System.Collections.Generic;

namespace TradingDashboard.Services.Strategies
{
    public enum StrategyStagedExitAction
    {
        None,
        ScaleOutFirst,
        ScaleOutSecond,
        ReduceHalfWarning,
        TrailAll,
        StopAll,
        BreakEvenAll,
        CloseAll
    }

    public sealed record StrategyStagedExitInput(
        double ProfitRate,
        double MinProfitRate,
        double HighDrawdownRate,
        bool FirstScaleOutDone,
        bool SecondScaleOutDone,
        bool IsClosingTime,
        long EntryPrice = 0,
        long CurrentPrice = 0,
        long FiveMinuteBaseLow = 0)
    {
        public static StrategyStagedExitInput Empty { get; } =
            new(0, 0, 0, false, false, false);
    }

    public sealed record StrategyStagedExitEvaluation(
        bool IsReady,
        StrategyStagedExitAction Action,
        bool ShouldSell,
        bool IsFullExit,
        int SellWeightPercent,
        bool OneMinuteWeak,
        bool FiveMinuteWeak,
        bool FifteenMinuteWeak,
        bool FiveMinuteBaseLowBroken,
        string Reason)
    {
        public static StrategyStagedExitEvaluation Empty(string reason) =>
            new(false, StrategyStagedExitAction.None, false, false, 0, false, false, false, false, reason);
    }

    public static class StrategyStagedExitEvaluator
    {
        public static StrategyStagedExitEvaluation Evaluate(
            StrategyStagedExitInput input,
            IReadOnlyList<StrategyMinuteBar> oneMinuteBars,
            IReadOnlyList<StrategyMinuteBar> fiveMinuteBars,
            IReadOnlyList<StrategyMinuteBar> fifteenMinuteBars,
            double hardStopRate = -1.2,
            double firstTargetRate = 1.0,
            double secondTargetRate = 2.0,
            double trailingStartRate = 1.0,
            double trailingDrawdownRate = 1.0,
            double breakEvenRecoveryFloor = -1.0)
        {
            bool hasPositionPrice = input.EntryPrice > 0 || input.CurrentPrice > 0 || Math.Abs(input.ProfitRate) > 0.0001;
            if (!hasPositionPrice)
                return StrategyStagedExitEvaluation.Empty("position exit state missing");

            bool oneMinuteWeak = IsCloseBelowMa5(oneMinuteBars);
            bool fiveMinuteWeak = IsCloseBelowMa5(fiveMinuteBars);
            bool fifteenMinuteWeak = IsCloseBelowMa5(fifteenMinuteBars);
            bool fiveMinuteBaseLowBroken = IsFiveMinuteBaseLowBroken(input, fiveMinuteBars);

            if (input.ProfitRate <= hardStopRate)
            {
                return Build(
                    StrategyStagedExitAction.StopAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"hard stop {input.ProfitRate:0.##}% <= {hardStopRate:0.##}%");
            }

            if (fiveMinuteBaseLowBroken)
            {
                return Build(
                    StrategyStagedExitAction.StopAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"5m base low broken: current {input.CurrentPrice:N0} / base low {input.FiveMinuteBaseLow:N0}");
            }

            if (fiveMinuteWeak && input.ProfitRate < 0)
            {
                return Build(
                    StrategyStagedExitAction.StopAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"5m flow weak while loss {input.ProfitRate:0.##}%");
            }

            if (fifteenMinuteWeak)
            {
                return Build(
                    StrategyStagedExitAction.StopAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    "15m flow damaged");
            }

            if (oneMinuteWeak && input.ProfitRate < firstTargetRate && input.ProfitRate > 0)
            {
                return Build(
                    StrategyStagedExitAction.ReduceHalfWarning,
                    false,
                    50,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"1m trigger weak, consider 30~50% reduction: pnl {input.ProfitRate:0.##}%");
            }

            if (input.ProfitRate >= firstTargetRate && !input.FirstScaleOutDone)
            {
                return Build(
                    StrategyStagedExitAction.ScaleOutFirst,
                    false,
                    30,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"+{firstTargetRate:0.##}% first scale-out");
            }

            if (input.ProfitRate >= secondTargetRate && !input.SecondScaleOutDone)
            {
                return Build(
                    StrategyStagedExitAction.ScaleOutSecond,
                    false,
                    50,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"+{secondTargetRate:0.##}% second scale-out");
            }

            if (input.ProfitRate >= trailingStartRate && input.HighDrawdownRate >= trailingDrawdownRate)
            {
                return Build(
                    StrategyStagedExitAction.TrailAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"trail stop: pnl {input.ProfitRate:0.##}%, high drawdown {input.HighDrawdownRate:0.##}%");
            }

            if (input.MinProfitRate <= breakEvenRecoveryFloor && input.ProfitRate >= 0)
            {
                return Build(
                    StrategyStagedExitAction.BreakEvenAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    $"break-even recovery: min {input.MinProfitRate:0.##}%, now {input.ProfitRate:0.##}%");
            }

            if (input.IsClosingTime)
            {
                return Build(
                    StrategyStagedExitAction.CloseAll,
                    true,
                    100,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fifteenMinuteWeak,
                    fiveMinuteBaseLowBroken,
                    "closing time full exit");
            }

            return Build(
                StrategyStagedExitAction.None,
                false,
                0,
                oneMinuteWeak,
                fiveMinuteWeak,
                fifteenMinuteWeak,
                fiveMinuteBaseLowBroken,
                $"hold: pnl {input.ProfitRate:0.##}%, high drawdown {input.HighDrawdownRate:0.##}%");
        }

        public static string ToProgressLabel(StrategyStagedExitEvaluation evaluation)
        {
            if (!evaluation.IsReady)
                return evaluation.Reason;

            string action = evaluation.Action switch
            {
                StrategyStagedExitAction.ScaleOutFirst => "scale 1",
                StrategyStagedExitAction.ScaleOutSecond => "scale 2",
                StrategyStagedExitAction.ReduceHalfWarning => "1m weak reduce",
                StrategyStagedExitAction.TrailAll => "trail all",
                StrategyStagedExitAction.StopAll => "stop all",
                StrategyStagedExitAction.BreakEvenAll => "breakeven all",
                StrategyStagedExitAction.CloseAll => "close all",
                _ => "hold"
            };

            string weakness =
                $"{(evaluation.OneMinuteWeak ? "1m-" : "1m+")}/" +
                $"{(evaluation.FiveMinuteWeak ? "5m-" : "5m+")}/" +
                $"{(evaluation.FifteenMinuteWeak ? "15m-" : "15m+")}";
            return $"{action} {evaluation.SellWeightPercent}% / {weakness}";
        }

        private static StrategyStagedExitEvaluation Build(
            StrategyStagedExitAction action,
            bool isFullExit,
            int sellWeightPercent,
            bool oneMinuteWeak,
            bool fiveMinuteWeak,
            bool fifteenMinuteWeak,
            bool fiveMinuteBaseLowBroken,
            string reason)
        {
            return new(
                true,
                action,
                action != StrategyStagedExitAction.None,
                isFullExit,
                sellWeightPercent,
                oneMinuteWeak,
                fiveMinuteWeak,
                fifteenMinuteWeak,
                fiveMinuteBaseLowBroken,
                reason);
        }

        private static bool IsCloseBelowMa5(IReadOnlyList<StrategyMinuteBar> bars)
        {
            if (bars.Count == 0)
                return false;

            StrategyMinuteBar current = bars[^1];
            return current.Close > 0 && current.Ma5 > 0 && current.Close < current.Ma5;
        }

        private static bool IsFiveMinuteBaseLowBroken(
            StrategyStagedExitInput input,
            IReadOnlyList<StrategyMinuteBar> fiveMinuteBars)
        {
            if (input.FiveMinuteBaseLow <= 0)
                return false;

            long currentPrice = input.CurrentPrice;
            if (currentPrice <= 0 && fiveMinuteBars.Count > 0)
                currentPrice = fiveMinuteBars[^1].Close;

            return currentPrice > 0 && currentPrice <= input.FiveMinuteBaseLow;
        }
    }
}
