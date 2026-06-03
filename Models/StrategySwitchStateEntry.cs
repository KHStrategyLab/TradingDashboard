namespace TradingDashboard.Models
{
    public sealed class StrategySwitchStateEntry
    {
        public string Key { get; set; } = string.Empty;
        public bool IsChecked { get; set; }
        public string TextValue { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
