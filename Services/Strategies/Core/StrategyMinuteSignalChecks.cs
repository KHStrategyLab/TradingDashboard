using System;

namespace TradingDashboard.Services.Strategies
{
    public readonly record struct StrategyMinuteBreakoutCheck(
        bool HasMinuteData,
        bool Ma60Ready,
        bool BreakoutReady,
        bool AboveMa60,
        bool Ma60Recovery,
        bool BreakoutTriggered,
        long SignalPrice,
        double Ma60,
        long BreakoutPrice)
    {
        public bool HasSignal => HasMinuteData && Ma60Recovery && BreakoutTriggered;

        public string FormatReadiness(int ma60Minute, int breakoutMinute)
        {
            string ma60Text = Ma60Ready ? "MA60 ready" : "MA60 wait";
            string breakoutText = BreakoutReady ? "20H ready" : "20H wait";
            return $"{ma60Minute}m:{ma60Text} / {breakoutMinute}m:{breakoutText}";
        }

        public string FormatSummary(string label, int ma60Minute, int breakoutMinute)
        {
            string state = HasSignal
                ? "SIGNAL"
                : HasMinuteData
                    ? "tracking"
                    : "waiting";
            return $"{label} {state} / price {FormatNumber(SignalPrice)} / MA60 {Ma60:0.##} / {breakoutMinute}m 20H {FormatNumber(BreakoutPrice)}";
        }

        private static string FormatNumber(long value) =>
            value > 0 ? value.ToString("N0") : "-";
    }

    public static class StrategyMinuteSignalChecks
    {
        public static StrategyMinuteBreakoutCheck EvaluateMa60Breakout(
            StrategyEvaluationContext context,
            int ma60Minute,
            int breakoutMinute)
        {
            StrategyMinuteFrameSnapshot? ma60Frame = context.MinuteSnapshots?.Get(ma60Minute);
            StrategyMinuteFrameSnapshot? breakoutFrame = context.MinuteSnapshots?.Get(breakoutMinute);
            bool ma60Ready = ma60Frame?.HasMa60 == true;
            bool breakoutReady = breakoutFrame?.HasBreakout20 == true;
            bool hasMinuteData = ma60Ready && breakoutReady;

            long signalPrice = ResolveSignalPrice(context, breakoutFrame, ma60Frame);
            double ma60 = ResolveMa60(ma60Frame);
            long breakoutPrice = breakoutFrame?.High20 ?? 0;

            bool aboveMa60 = signalPrice > 0 && ma60 > 0 && signalPrice >= ma60;
            bool touchedMa60 = HasRecentMa60Touch(ma60Frame, ma60);
            bool ma60Recovery = ma60Ready && aboveMa60 && touchedMa60;
            bool breakoutTriggered = breakoutReady && signalPrice > 0 && breakoutPrice > 0 && signalPrice > breakoutPrice;

            return new StrategyMinuteBreakoutCheck(
                hasMinuteData,
                ma60Ready,
                breakoutReady,
                aboveMa60,
                ma60Recovery,
                breakoutTriggered,
                signalPrice,
                ma60,
                breakoutPrice);
        }

        private static long ResolveSignalPrice(
            StrategyEvaluationContext context,
            StrategyMinuteFrameSnapshot? breakoutFrame,
            StrategyMinuteFrameSnapshot? ma60Frame)
        {
            if (breakoutFrame?.CurrentClose > 0)
                return breakoutFrame.CurrentClose;
            if (ma60Frame?.CurrentClose > 0)
                return ma60Frame.CurrentClose;
            if (context.Stock?.CurrentPrice > 0)
                return context.Stock.CurrentPrice;
            return context.Stock?.LastPrice ?? 0;
        }

        private static double ResolveMa60(StrategyMinuteFrameSnapshot? frame)
        {
            if (frame == null)
                return 0;
            if (frame.Ma60 > 0)
                return frame.Ma60;
            return frame.LastCompletedMa60;
        }

        private static bool HasRecentMa60Touch(StrategyMinuteFrameSnapshot? frame, double ma60)
        {
            if (frame == null || ma60 <= 0)
                return false;

            bool currentTouches = frame.CurrentLow > 0 &&
                frame.CurrentHigh > 0 &&
                frame.CurrentLow <= ma60 &&
                frame.CurrentHigh >= ma60;
            bool completedTouches = frame.LastCompletedLow > 0 &&
                frame.LastCompletedHigh > 0 &&
                frame.LastCompletedMa60 > 0 &&
                frame.LastCompletedLow <= frame.LastCompletedMa60 &&
                frame.LastCompletedHigh >= frame.LastCompletedMa60;
            bool completedRecovered = frame.LastCompletedClose > 0 &&
                frame.LastCompletedMa60 > 0 &&
                frame.LastCompletedClose <= frame.LastCompletedMa60;

            return currentTouches || completedTouches || completedRecovered;
        }
    }
}
