using System.Collections.Generic;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteBars : Dictionary<int, IReadOnlyList<StrategyMinuteBar>>
    {
        public StrategyMinuteBars()
        {
        }

        public StrategyMinuteBars(IDictionary<int, IReadOnlyList<StrategyMinuteBar>> source)
            : base(source)
        {
        }

        public IReadOnlyList<StrategyMinuteBar> Get(int minute) =>
            TryGetValue(minute, out IReadOnlyList<StrategyMinuteBar>? bars) ? bars : [];
    }
}
