using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteDataStatus
    {
        private readonly Dictionary<int, int> _counts = [];
        private readonly Dictionary<int, int> _targets = [];

        public void SetCount(int minute, int count, int targetCount = 20)
        {
            if (minute <= 0)
                return;

            _counts[minute] = count < 0 ? 0 : count;
            _targets[minute] = targetCount <= 0 ? 20 : targetCount;
        }

        public int GetCount(int minute) =>
            _counts.TryGetValue(minute, out int count) ? count : 0;

        public int GetTargetCount(int minute) =>
            _targets.TryGetValue(minute, out int target) ? target : 20;

        public bool Has(int minute, int minimumCount = 20) =>
            GetCount(minute) >= Math.Max(minimumCount, GetTargetCount(minute));

        public bool HasAll(params int[] minutes) =>
            minutes.All(minute => Has(minute));

        public string Format(params int[] minutes)
        {
            if (minutes.Length == 0)
                return "minute data -";

            return string.Join(" / ", minutes.Select(minute => $"{minute}m:{GetCount(minute):N0}/{GetTargetCount(minute):N0}"));
        }
    }
}
