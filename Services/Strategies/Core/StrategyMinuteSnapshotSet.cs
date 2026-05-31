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

        public bool HasMa60AndBreakout20(int ma60Minute, int breakoutMinute) =>
            Get(ma60Minute)?.HasMa60 == true &&
            Get(breakoutMinute)?.HasBreakout20 == true;

        public string FormatMa60AndBreakout20(int ma60Minute, int breakoutMinute)
        {
            StrategyMinuteFrameSnapshot? ma60Frame = Get(ma60Minute);
            StrategyMinuteFrameSnapshot? breakoutFrame = Get(breakoutMinute);
            string ma60Text = ma60Frame?.HasMa60 == true ? "MA60 ready" : "MA60 wait";
            string breakoutText = breakoutFrame?.HasBreakout20 == true ? "20H ready" : "20H wait";
            return $"{ma60Minute}m:{ma60Text} / {breakoutMinute}m:{breakoutText}";
        }
    }
}
