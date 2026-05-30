using System.Collections.Generic;

namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyProgressSnapshot(
        StrategySlotId SlotId,
        string StateKey,
        string StateText,
        int CurrentStep,
        int TotalSteps,
        double ProgressPercent,
        string LevelText,
        double StrengthPercent,
        string StrengthLabel,
        IReadOnlyList<StrategyProgressStep> Steps)
    {
        public static StrategyProgressSnapshot Empty(StrategySlotId slotId) =>
            new(slotId, "WAIT", "대기", 0, 0, 0, "-", 0, "기준강도", []);

        public bool IsBeforeBuy => ProgressPercent < 70;

        public bool IsBuyFilledPoint => ProgressPercent == 70;

        public bool IsAfterBuy => ProgressPercent > 70;
    }
}
