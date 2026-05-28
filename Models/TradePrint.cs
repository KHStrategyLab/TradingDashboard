using System.Windows.Media;

namespace TradingDashboard.Models
{
    public class TradePrint
    {
        public string PriceText { get; set; } = "-";
        public string QuantityText { get; set; } = "-";
        public Brush Color { get; set; } = Brushes.White;
        public Brush QuantityColor { get; set; } = Brushes.White;
    }
}
