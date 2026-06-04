using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public static class StrategyExitStrategyRegistry
    {
        public const string BaseCandleLowProfitScale = "BASE_LOW_PROFIT_SCALE";
        public const string SimplePlus6Minus2 = "SIMPLE_PLUS6_MINUS2";
        public const string QuickReactionReentry = "QUICK_REACTION_REENTRY";
        public const string ProfitScaleTrail = "PROFIT_SCALE_TRAIL";
        public const string SwingHold = "SWING_HOLD";
        public const string ManualBuyStopAssist = "MANUAL_BUY_STOP_ASSIST";

        private static readonly IReadOnlyList<StrategyExitStrategyDescriptor> Descriptors =
        [
            new(
                BaseCandleLowProfitScale,
                "Base Scale",
                "Shared exit profile: early scale and final target"),
            new(
                SimplePlus6Minus2,
                "+6 / -2",
                "Shared exit profile: full target at +6%, stop at -2%"),
            new(
                QuickReactionReentry,
                "Quick Reaction Re-entry",
                "Shared exit profile: quick no-go cut and re-entry wait"),
            new(
                ProfitScaleTrail,
                "Scale + Trail",
                "Shared exit profile: scale out, then trail the rest"),
            new(
                SwingHold,
                "Swing Hold",
                "Shared exit profile: wider stop for longer hold"),
            new(
                ManualBuyStopAssist,
                "Manual Stop Assist",
                "Manual or untagged holdings only",
                IsManualOnly: true)
        ];

        public static IReadOnlyList<StrategyExitStrategyDescriptor> GetDescriptors(bool includeManualOnly = false) =>
            includeManualOnly ? Descriptors : [.. Descriptors.Where(x => !x.IsManualOnly)];

        public static StrategyExitStrategyDescriptor Resolve(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                StrategyExitStrategyDescriptor? descriptor = Descriptors.FirstOrDefault(x => x.Code == code);
                if (descriptor != null)
                    return descriptor;
            }

            return Descriptors[0];
        }

        public static string GetDefaultForSlot(StrategySlotId slotId) =>
            slotId switch
            {
                StrategySlotId.BaseCandleChase => QuickReactionReentry,
                StrategySlotId.ThreeMinutePullback => BaseCandleLowProfitScale,
                StrategySlotId.SorTenMinuteFiveMinuteBreakout => QuickReactionReentry,
                StrategySlotId.IntradayFifteenMinuteScalp => ProfitScaleTrail,
                StrategySlotId.IntradayFiveMinuteStableScalp => ProfitScaleTrail,
                StrategySlotId.ThemeDisclosureAssist => ManualBuyStopAssist,
                _ => BaseCandleLowProfitScale
            };
    }
}
