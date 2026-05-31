using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public sealed class StrategyMinuteSeedFileDocument
    {
        public string Code { get; set; } = string.Empty;
        public string Market { get; set; } = string.Empty;
        public int Minute { get; set; }
        public string SeedDate { get; set; } = string.Empty;
        public string SavedAt { get; set; } = string.Empty;
        public List<DailyCandle> Candles { get; set; } = [];
    }
}
