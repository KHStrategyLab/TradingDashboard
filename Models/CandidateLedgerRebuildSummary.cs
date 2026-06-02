using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class CandidateLedgerRebuildSummary
    {
        public string RunId { get; set; } = string.Empty;
        public int LookbackTradingDays { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public int DailyBarCount { get; set; }
        public int CandidateCount { get; set; }
        public int ActiveCandidateCount { get; set; }
        public int ArchiveCandidateCount { get; set; }
        public Dictionary<string, int> SourceCounts { get; set; } = new();
        public Dictionary<string, int> DailyMetricsStatusCounts { get; set; } = new();
        public List<string> Logs { get; set; } = [];
    }
}
