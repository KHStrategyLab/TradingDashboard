using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TradingDashboard.Models
{
    public class WatchStockItem : INotifyPropertyChanged
    {
        private string _code = string.Empty;
        private string _name = string.Empty;
        private long _currentPrice;
        private long _changeAmount;
        private string _changeRateText = "-";
        private string _volumeText = "-";
        private Brush _priceBrush = Brushes.White;
        private bool _supportsNxt;

        public string Code
        {
            get => _code;
            set => SetField(ref _code, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public long CurrentPrice
        {
            get => _currentPrice;
            set
            {
                if (SetField(ref _currentPrice, value))
                    OnPropertyChanged(nameof(CurrentPriceText));
            }
        }

        public long ChangeAmount
        {
            get => _changeAmount;
            set
            {
                if (SetField(ref _changeAmount, value))
                {
                    OnPropertyChanged(nameof(ChangeAmountText));
                    OnPropertyChanged(nameof(DirectionText));
                }
            }
        }

        public string ChangeRateText
        {
            get => _changeRateText;
            set => SetField(ref _changeRateText, value);
        }

        public string VolumeText
        {
            get => _volumeText;
            set => SetField(ref _volumeText, value);
        }

        public Brush PriceBrush
        {
            get => _priceBrush;
            set => SetField(ref _priceBrush, value);
        }

        public bool SupportsNxt
        {
            get => _supportsNxt;
            set => SetField(ref _supportsNxt, value);
        }

        public string CurrentPriceText => CurrentPrice > 0 ? CurrentPrice.ToString("N0") : "-";
        public string ChangeAmountText => ChangeAmount == 0 ? "0" : (ChangeAmount > 0 ? $"+{ChangeAmount:N0}" : $"{ChangeAmount:N0}");
        public string DirectionText => ChangeAmount > 0 ? "▲ " : ChangeAmount < 0 ? "▼ " : "■ ";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
