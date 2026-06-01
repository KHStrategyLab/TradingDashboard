using System;

namespace TradingDashboard.Models
{
    public sealed class BacktestCandidate
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "KRX";
        public string CandidateDate { get; set; } = string.Empty;
        public bool NxtEnabled { get; set; }
        public string StrategyCode { get; set; } = "BASE_CANDLE";
        public string Source { get; set; } = "UNKNOWN";
        public string SourceName { get; set; } = string.Empty;
        public string Memo { get; set; } = string.Empty;
        public DateTime ImportedAt { get; set; } = DateTime.Now;
    }
}
