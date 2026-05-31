using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategySlotRegistry
    {
        private readonly Dictionary<StrategySlotId, ITradingStrategySlot> _slots;

        private StrategySlotRegistry(IEnumerable<ITradingStrategySlot> slots)
        {
            _slots = slots.ToDictionary(x => x.Id);
        }

        public static StrategySlotRegistry CreateDefault() =>
            new(
            [
                new SorTenMinuteMa60ThreeMinuteBreakoutAggressiveStrategySlot(),
                new SorFifteenMinuteMa60FiveMinuteBreakoutStrategySlot(),
                new SorTenMinuteMa60FiveMinuteBreakoutStrategySlot(),
                new ThemeDisclosureAssistStrategySlot()
            ]);

        public IReadOnlyList<StrategySlotDescriptor> GetDescriptors() =>
            [.. _slots.Values.Select(x => x.Descriptor)];

        public StrategySlotDescriptor? GetDescriptor(StrategySlotId id) =>
            _slots.TryGetValue(id, out ITradingStrategySlot? slot)
                ? slot.Descriptor
                : null;

        public IReadOnlyList<StrategyEvaluationResult> EvaluateEnabled(
            IEnumerable<StrategySlotSetting> settings,
            StrategyEvaluationContext context)
        {
            List<StrategyEvaluationResult> results = [];

            foreach (StrategySlotSetting setting in settings)
            {
                if (!_slots.TryGetValue(setting.Id, out ITradingStrategySlot? slot))
                    continue;

                results.Add(setting.IsEnabled
                    ? slot.Evaluate(context)
                    : StrategyEvaluationResult.Ignored(slot.Id, slot.Name));
            }

            return results;
        }
    }
}
