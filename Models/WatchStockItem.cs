using System.Collections.Generic;
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
        private string _marketTypeCode = string.Empty;
        private string _marketName = string.Empty;
        private string _programMarketType = string.Empty;
        private string _orderWarning = string.Empty;
        private string _auditInfo = string.Empty;
        private string _stockState = string.Empty;
        private string _sectorName = string.Empty;
        private long _lastPrice;
        private Brush _priceBrush = Brushes.White;
        private bool _supportsNxt;

        public string Code
        {
            get => _code;
            set => SetField(ref _code, NormalizeText(value));
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, NormalizeText(value));
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
            set => SetField(ref _changeRateText, NormalizeDash(value));
        }

        public string VolumeText
        {
            get => _volumeText;
            set => SetField(ref _volumeText, NormalizeDash(value));
        }

        public string MarketTypeCode
        {
            get => _marketTypeCode;
            set
            {
                if (SetField(ref _marketTypeCode, NormalizeText(value)))
                {
                    OnPropertyChanged(nameof(MarketBadgeText));
                    OnPropertyChanged(nameof(MetaBadgeText));
                }
            }
        }

        public string MarketName
        {
            get => _marketName;
            set
            {
                if (SetField(ref _marketName, NormalizeText(value)))
                {
                    OnPropertyChanged(nameof(MarketBadgeText));
                    OnPropertyChanged(nameof(MetaBadgeText));
                }
            }
        }

        public string ProgramMarketType
        {
            get => _programMarketType;
            set => SetField(ref _programMarketType, NormalizeText(value));
        }

        public string OrderWarning
        {
            get => _orderWarning;
            set
            {
                if (SetField(ref _orderWarning, NormalizeText(value)))
                {
                    OnPropertyChanged(nameof(OrderWarningBadgeText));
                    OnPropertyChanged(nameof(OrderWarningListBadgeText));
                    OnPropertyChanged(nameof(AlertListBadgeText));
                    OnPropertyChanged(nameof(MetaBadgeText));
                }
            }
        }

        public string AuditInfo
        {
            get => _auditInfo;
            set
            {
                if (SetField(ref _auditInfo, NormalizeText(value)))
                {
                    OnPropertyChanged(nameof(AuditInfoBadgeText));
                    OnPropertyChanged(nameof(AlertListBadgeText));
                    OnPropertyChanged(nameof(MetaBadgeText));
                }
            }
        }

        public string StockState
        {
            get => _stockState;
            set
            {
                if (SetField(ref _stockState, NormalizeText(value)))
                    OnPropertyChanged(nameof(MetaBadgeText));
            }
        }

        public string SectorName
        {
            get => _sectorName;
            set
            {
                if (SetField(ref _sectorName, NormalizeText(value)))
                    OnPropertyChanged(nameof(MetaBadgeText));
            }
        }

        public long LastPrice
        {
            get => _lastPrice;
            set
            {
                if (SetField(ref _lastPrice, value))
                    OnPropertyChanged(nameof(LastPriceText));
            }
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
        public string ChangeAmountText => ChangeAmount == 0 ? "0" : ChangeAmount > 0 ? $"+{ChangeAmount:N0}" : $"{ChangeAmount:N0}";
        public string DirectionText => ChangeAmount > 0 ? "+" : ChangeAmount < 0 ? "-" : "";
        public string LastPriceText => LastPrice > 0 ? LastPrice.ToString("N0") : "-";
        public string MarketBadgeText => string.IsNullOrWhiteSpace(MarketName) ? MarketTypeCode : NormalizeMarketName(MarketName);
        public string OrderWarningBadgeText => FormatOrderWarning(OrderWarning);
        public string OrderWarningListBadgeText => NormalizeText(OrderWarning) == "5" ? "WARN" : OrderWarningBadgeText;
        public string AuditInfoBadgeText => FormatAuditInfo(AuditInfo);
        public string AlertListBadgeText => !string.IsNullOrWhiteSpace(OrderWarningListBadgeText)
            ? OrderWarningListBadgeText
            : AuditInfoBadgeText == "CAUTION" ? "CAUT" : AuditInfoBadgeText;
        public string MetaBadgeText
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(MarketBadgeText))
                    parts.Add(MarketBadgeText);
                if (!string.IsNullOrWhiteSpace(SectorName))
                    parts.Add(SectorName);
                if (!string.IsNullOrWhiteSpace(StockState))
                    parts.Add(StockState);
                if (!string.IsNullOrWhiteSpace(AuditInfoBadgeText))
                    parts.Add(AuditInfoBadgeText);
                return parts.Count == 0 ? "-" : string.Join(" · ", parts);
            }
        }

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

        private static string NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string NormalizeMarketName(string value)
        {
            string text = NormalizeText(value);
            return text switch
            {
                "Exchange" => string.Empty,
                "KOSPI" => string.Empty,
                "거래소" => string.Empty,
                "코스피" => string.Empty,
                _ => text
            };
        }

        private static string FormatOrderWarning(string? value)
        {
            string code = NormalizeText(value);
            return code switch
            {
                "" => string.Empty,
                "0" => string.Empty,
                "1" => "ETF",
                "2" => "CLEAN",
                "3" => "HOT",
                "4" => "RISK",
                "5" => "WARN",
                _ => code
            };
        }

        private static string FormatAuditInfo(string? value)
        {
            string text = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(text) || text == "Normal" || text == "정상")
                return string.Empty;
            if (text.Contains("단기과열") || text.Contains("Hot", StringComparison.OrdinalIgnoreCase))
                return "HOT";
            if (text.Contains("투자주의") || text.Contains("환기") || text.Contains("Caution", StringComparison.OrdinalIgnoreCase))
                return "CAUTION";
            return text;
        }
    }
}
