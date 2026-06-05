namespace TradingDashboard.Models
{
    public sealed class BacktestDatasetUpdateSummary
    {
        public string RunId { get; set; } = string.Empty;
        public string MarketMode { get; set; } = string.Empty;
        public int CandidateCount { get; set; }
        public int DailyDownloadCount { get; set; }
        public int DailyReusedCount { get; set; }
        public int DailyBarUpsertCount { get; set; }
        public int BaseCandleCount { get; set; }
        public List<string> Logs { get; set; } = [];
    }
}
