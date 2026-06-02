using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class CandidateLedgerEnrichSummary
    {
        public string RunId { get; set; } = string.Empty;
        public int CandidateCount { get; set; }
        public int EnrichedCount { get; set; }
        public int FailedCount { get; set; }
        public int PendingCount { get; set; }
        public Dictionary<string, int> FundamentalStatusCounts { get; set; } = new();
        public List<string> Logs { get; set; } = [];
    }
}
