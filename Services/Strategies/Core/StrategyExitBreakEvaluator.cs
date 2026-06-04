using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public enum StrategyExitBreakState
    {
        None,
        FirstBreak,
        TrapCandidate,
        Recovered,
        SecondBreak,
        StrongBreak,
        ReboundRejected
    }

    public sealed record StrategyExitBreakEvaluation(
        StrategyExitBreakState State,
        bool IsBroken,
        bool IsStrongExit,
        bool ShouldExit,
        long BasePrice,
        long CurrentClose,
        int BreakCount,
        double VolumeRatio,
        string Reason)
    {
        public static StrategyExitBreakEvaluation Empty(string reason = "no exit base") =>
            new(StrategyExitBreakState.None, false, false, false, 0, 0, 0, 0, reason);
    }

    public static class StrategyExitBreakEvaluator
    {
        public static StrategyExitBreakEvaluation Evaluate(
            IReadOnlyList<StrategyMinuteBar> bars,
            long basePrice,
            int lookbackBars = 12,
            double strongVolumeMultiple = 1.8,
            double reboundNearPercent = 0.003)
        {
            if (basePrice <= 0)
                return StrategyExitBreakEvaluation.Empty();
            if (bars.Count == 0)
                return StrategyExitBreakEvaluation.Empty("no minute bars");

            StrategyMinuteBar current = bars[^1];
            if (current.Close <= 0)
                return StrategyExitBreakEvaluation.Empty("no current close");

            List<StrategyMinuteBar> recent = [.. bars
                .Where(bar => bar.Close > 0)
                .TakeLast(Math.Max(1, lookbackBars))];
            int breakCount = recent.Count(bar => bar.Close < basePrice);
            bool currentBreak = current.Close < basePrice;
            bool previousBreak = recent.Count >= 2 && recent[^2].Close < basePrice;
            double volumeRatio = CalculateVolumeRatio(bars);

            bool nearBase = current.High >= basePrice * (1.0 - reboundNearPercent);
            bool reboundRejected = currentBreak && nearBase && previousBreak;
            bool secondBreak = currentBreak && breakCount >= 2;
            bool volumeStrongBreak = currentBreak && volumeRatio >= strongVolumeMultiple;
            bool strongBreak = currentBreak && (volumeStrongBreak || secondBreak || reboundRejected);
            bool trapCandidate = currentBreak && !strongBreak && volumeRatio < 1.2;
            bool recovered = !currentBreak && breakCount > 0;

            StrategyExitBreakState state =
                reboundRejected ? StrategyExitBreakState.ReboundRejected :
                volumeStrongBreak ? StrategyExitBreakState.StrongBreak :
                secondBreak ? StrategyExitBreakState.SecondBreak :
                trapCandidate ? StrategyExitBreakState.TrapCandidate :
                currentBreak ? StrategyExitBreakState.FirstBreak :
                recovered ? StrategyExitBreakState.Recovered :
                StrategyExitBreakState.None;

            bool shouldExit = state is
                StrategyExitBreakState.StrongBreak or
                StrategyExitBreakState.SecondBreak or
                StrategyExitBreakState.ReboundRejected;

            return new StrategyExitBreakEvaluation(
                state,
                currentBreak,
                strongBreak,
                shouldExit,
                basePrice,
                current.Close,
                breakCount,
                volumeRatio,
                FormatReason(state, basePrice, current.Close, breakCount, volumeRatio));
        }

        public static string ToProgressLabel(StrategyExitBreakEvaluation evaluation)
        {
            if (evaluation.BasePrice <= 0)
                return "exit base missing";

            string state = evaluation.State switch
            {
                StrategyExitBreakState.None => "base alive",
                StrategyExitBreakState.FirstBreak => "first break",
                StrategyExitBreakState.TrapCandidate => "trap candidate",
                StrategyExitBreakState.Recovered => "base recovered",
                StrategyExitBreakState.SecondBreak => "second break",
                StrategyExitBreakState.StrongBreak => "strong break",
                StrategyExitBreakState.ReboundRejected => "rebound rejected",
                _ => "exit check"
            };

            return $"{state} {evaluation.CurrentClose:N0}/{evaluation.BasePrice:N0} v{evaluation.VolumeRatio:0.##}x";
        }

        private static double CalculateVolumeRatio(IReadOnlyList<StrategyMinuteBar> bars)
        {
            StrategyMinuteBar current = bars[^1];
            List<StrategyMinuteBar> previous = [.. bars
                .Take(Math.Max(0, bars.Count - 1))
                .Where(bar => bar.Volume > 0)
                .TakeLast(20)];

            if (previous.Count == 0 || current.Volume <= 0)
                return 0;

            double average = previous.Average(bar => bar.Volume);
            return average > 0 ? current.Volume / average : 0;
        }

        private static string FormatReason(
            StrategyExitBreakState state,
            long basePrice,
            long currentClose,
            int breakCount,
            double volumeRatio)
        {
            return state switch
            {
                StrategyExitBreakState.StrongBreak => $"strong base break: close {currentClose:N0} < base {basePrice:N0}, volume {volumeRatio:0.##}x, breaks {breakCount}",
                StrategyExitBreakState.SecondBreak => $"second base break: close {currentClose:N0} < base {basePrice:N0}, breaks {breakCount}",
                StrategyExitBreakState.ReboundRejected => $"rebound rejected below base: close {currentClose:N0}, base {basePrice:N0}",
                StrategyExitBreakState.FirstBreak => $"first base break: close {currentClose:N0} < base {basePrice:N0}",
                StrategyExitBreakState.TrapCandidate => $"first low-volume break candidate: close {currentClose:N0}, base {basePrice:N0}",
                StrategyExitBreakState.Recovered => $"base recovered after break: close {currentClose:N0} >= base {basePrice:N0}",
                _ => $"base alive: close {currentClose:N0}, base {basePrice:N0}"
            };
        }
    }
}
