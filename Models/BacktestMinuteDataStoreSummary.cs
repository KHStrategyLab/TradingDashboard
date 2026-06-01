using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class BacktestMinuteDataStoreSummary
    {
        public string RunId { get; set; } = string.Empty;
        public int BaseCandleCount { get; set; }
        public int StockMarketCount { get; set; }
        public int MinuteSetCount { get; set; }
        public int MinuteDownloadCount { get; set; }
        public int MinuteReusedCount { get; set; }
        public int MinuteBarUpsertCount { get; set; }
        public List<string> Logs { get; set; } = [];
    }
}
