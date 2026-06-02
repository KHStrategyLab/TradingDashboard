using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class LeaderHistoryRebuildSummary
    {
        public string RunId { get; set; } = string.Empty;
        public int LookbackTradingDays { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public int DailyBarCount { get; set; }
        public int CandidateCount { get; set; }
        public int ActiveLeaderCount { get; set; }
        public Dictionary<string, int> GradeCounts { get; set; } = new();
        public Dictionary<string, int> LeaderTypeCounts { get; set; } = new();
        public List<string> Logs { get; set; } = [];
    }
}
