using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public static class StrategyExitStrategyRegistry
    {
        public const string BaseCandleLowProfitScale = "BASE_LOW_PROFIT_SCALE";
        public const string QuickReactionReentry = "QUICK_REACTION_REENTRY";
        public const string ProfitScaleTrail = "PROFIT_SCALE_TRAIL";
        public const string SwingHold = "SWING_HOLD";
        public const string ManualBuyStopAssist = "MANUAL_BUY_STOP_ASSIST";

        private static readonly IReadOnlyList<StrategyExitStrategyDescriptor> Descriptors =
        [
            new(
                BaseCandleLowProfitScale,
                "기준봉 저가 + 2/4% 분할",
                "기준봉 저가 이탈 또는 -2% 손절, +2% 1차, +4% 전량 기준"),
            new(
                QuickReactionReentry,
                "Quick Reaction Re-entry",
                "5분 안에 안 가면 정리하고 재진입 후보로 다시 기다리는 빠른 대응"),
            new(
                ProfitScaleTrail,
                "2/4% 익절 + 추적",
                "초기 익절 후 남은 수량은 추세 유지 여부로 추적"),
            new(
                SwingHold,
                "Swing Hold",
                "당일 초단타가 아니라 며칠 보유 가능한 실험용 청산 틀"),
            new(
                ManualBuyStopAssist,
                "수동매수 자동손절기",
                "수동 또는 꼬리표 없는 보유분만 5분봉 저가/MA5 이탈로 관리",
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
                StrategySlotId.ThemeDisclosureAssist => ManualBuyStopAssist,
                _ => BaseCandleLowProfitScale
            };
    }
}
