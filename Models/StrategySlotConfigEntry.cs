namespace TradingDashboard.Models
{
    public sealed class StrategySlotConfigEntry
    {
        public string SlotId { get; set; } = string.Empty;
        public bool? IsEnabled { get; set; }
        public string ExitStrategyCode { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
