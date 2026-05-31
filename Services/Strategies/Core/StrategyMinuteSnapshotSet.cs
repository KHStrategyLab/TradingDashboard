using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public sealed class StrategyMinuteSnapshotSet
    {
        private readonly Dictionary<int, StrategyMinuteFrameSnapshot> _frames;

        public StrategyMinuteSnapshotSet(string code, string market, IEnumerable<StrategyMinuteFrameSnapshot> frames)
        {
            Code = code;
            Market = market;
            _frames = frames
                .Where(frame => frame != null && frame.Minute > 0)
                .GroupBy(frame => frame.Minute)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public string Code { get; }

        public string Market { get; }

        public IReadOnlyDictionary<int, StrategyMinuteFrameSnapshot> Frames => _frames;

        public StrategyMinuteFrameSnapshot? Get(int minute) =>
            _frames.TryGetValue(minute, out StrategyMinuteFrameSnapshot? frame) ? frame : null;

        public bool HasAll(params int[] minutes) =>
            minutes.All(minute => Get(minute)?.IsReady == true);
    }
}
