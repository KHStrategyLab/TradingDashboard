using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace TradingDashboard.Models
{
    public class HogaLevel : INotifyPropertyChanged
    {
        private string _priceText = "-";
        private string _qtyText = "-";
        private string _rateText = string.Empty;
        private long _rawPrice;
        private Brush _priceBrush = Brushes.White;
        private Brush _rateBrush = Brushes.White;
        private Brush _currentPriceBackgroundBrush = Brushes.Transparent;
        private Brush _currentPriceBorderBrush = Brushes.Transparent;
        private Thickness _currentPriceBorderThickness = new(0);

        public string PriceText
        {
            get => _priceText;
            set => SetField(ref _priceText, value);
        }

        public string QtyText
        {
            get => _qtyText;
            set => SetField(ref _qtyText, value);
        }

        public string RateText
        {
            get => _rateText;
            set => SetField(ref _rateText, value);
        }

        public long RawPrice
        {
            get => _rawPrice;
            set => SetField(ref _rawPrice, value);
        }

        public Brush PriceBrush
        {
            get => _priceBrush;
            set => SetField(ref _priceBrush, value);
        }

        public Brush RateBrush
        {
            get => _rateBrush;
            set => SetField(ref _rateBrush, value);
        }

        public Brush CurrentPriceBorderBrush
        {
            get => _currentPriceBorderBrush;
            set => SetField(ref _currentPriceBorderBrush, value);
        }

        public Brush CurrentPriceBackgroundBrush
        {
            get => _currentPriceBackgroundBrush;
            set => SetField(ref _currentPriceBackgroundBrush, value);
        }

        public Thickness CurrentPriceBorderThickness
        {
            get => _currentPriceBorderThickness;
            set => SetField(ref _currentPriceBorderThickness, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
