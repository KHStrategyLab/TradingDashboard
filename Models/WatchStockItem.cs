using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TradingDashboard.Models
{
    public class WatchStockItem : INotifyPropertyChanged
    {
        private const long TradeValueSignalThreshold = 20_000_000_000;
        private string _code = string.Empty;
        private string _name = string.Empty;
        private long _currentPrice;
        private long _changeAmount;
        private string _changeRateText = "-";
        private string _volumeText = "-";
        private long _todayTradeValue;
        private string _marketTypeCode = string.Empty;
        private string _marketName = string.Empty;
        private string _programMarketType = string.Empty;
        private string _orderWarning = string.Empty;
        private string _auditInfo = string.Empty;
        private string _stockState = string.Empty;
        private string _sectorName = string.Empty;
        private string _displayPriceMarket = string.Empty;
        private long _krxDisplayPrice;
        private long _nxtDisplayPrice;
        private bool _gateBaseCandleFound;
        private int _gateBaseCandleOffset = -1;
        private string _gateBaseCandleDate = string.Empty;
        private string _gateBaseCandleMarket = string.Empty;
        private double _gateBaseCandleChangeRate;
        private long _gateBaseCandleTradeValue;
        private string _conditionEntryReason = string.Empty;
        private bool _isIntradayPreCandidate;
        private long _lastPrice;
        private long _miniDailyOpen;
        private long _miniDailyHigh;
        private long _miniDailyLow;
        private long _miniDailyClose;
        private string _miniDailyMarket = string.Empty;
        private Brush _priceBrush = Brushes.White;
        private Brush _miniDailyBrush = Brushes.Transparent;
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

        public long TodayTradeValue
        {
            get => _todayTradeValue;
            set
            {
                if (SetField(ref _todayTradeValue, value))
                {
                    OnPropertyChanged(nameof(TodayTradeValueText));
                    OnPropertyChanged(nameof(IsTodayTradeValueStrong));
                    if (value >= TradeValueSignalThreshold &&
                        string.Equals(ConditionEntryReason, "FORMING", StringComparison.OrdinalIgnoreCase))
                    {
                        ConditionEntryReason = "RANK_HOT";
                    }
                }
            }
        }

        public string DisplayPriceMarket
        {
            get => _displayPriceMarket;
            private set => SetField(ref _displayPriceMarket, NormalizeText(value));
        }

        public long KrxDisplayPrice
        {
            get => _krxDisplayPrice;
            private set => SetField(ref _krxDisplayPrice, value);
        }

        public long NxtDisplayPrice
        {
            get => _nxtDisplayPrice;
            private set => SetField(ref _nxtDisplayPrice, value);
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

        public bool GateBaseCandleFound
        {
            get => _gateBaseCandleFound;
            set
            {
                if (SetField(ref _gateBaseCandleFound, value))
                {
                    OnPropertyChanged(nameof(GateBaseCandleBadgeText));
                    OnPropertyChanged(nameof(GateBaseCandleDetailText));
                }
            }
        }

        public int GateBaseCandleOffset
        {
            get => _gateBaseCandleOffset;
            set
            {
                if (SetField(ref _gateBaseCandleOffset, value))
                {
                    OnPropertyChanged(nameof(GateBaseCandleBadgeText));
                    OnPropertyChanged(nameof(GateBaseCandleDetailText));
                }
            }
        }

        public string GateBaseCandleDate
        {
            get => _gateBaseCandleDate;
            set
            {
                if (SetField(ref _gateBaseCandleDate, NormalizeText(value)))
                {
                    OnPropertyChanged(nameof(GateBaseCandleBadgeText));
                    OnPropertyChanged(nameof(GateBaseCandleDetailText));
                }
            }
        }

        public string GateBaseCandleMarket
        {
            get => _gateBaseCandleMarket;
            set
            {
                if (SetField(ref _gateBaseCandleMarket, NormalizeText(value)))
                {
                    OnPropertyChanged(nameof(GateBaseCandleBadgeText));
                    OnPropertyChanged(nameof(GateBaseCandleDetailText));
                }
            }
        }

        public double GateBaseCandleChangeRate
        {
            get => _gateBaseCandleChangeRate;
            set
            {
                if (SetField(ref _gateBaseCandleChangeRate, value))
                    OnPropertyChanged(nameof(GateBaseCandleDetailText));
            }
        }

        public long GateBaseCandleTradeValue
        {
            get => _gateBaseCandleTradeValue;
            set
            {
                if (SetField(ref _gateBaseCandleTradeValue, value))
                    OnPropertyChanged(nameof(GateBaseCandleDetailText));
            }
        }

        public string ConditionEntryReason
        {
            get => _conditionEntryReason;
            set
            {
                if (SetField(ref _conditionEntryReason, NormalizeText(value).ToUpperInvariant()))
                {
                    OnPropertyChanged(nameof(ConditionEntryReasonBadgeText));
                    OnPropertyChanged(nameof(ConditionEntryReasonDetailText));
                }
            }
        }

        public bool IsIntradayPreCandidate
        {
            get => _isIntradayPreCandidate;
            set
            {
                if (SetField(ref _isIntradayPreCandidate, value))
                {
                    OnPropertyChanged(nameof(PreCandidateBadgeText));
                    OnPropertyChanged(nameof(GateBaseCandleBadgeText));
                }
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

        public long MiniDailyOpen
        {
            get => _miniDailyOpen;
            private set => SetField(ref _miniDailyOpen, value);
        }

        public long MiniDailyHigh
        {
            get => _miniDailyHigh;
            private set => SetField(ref _miniDailyHigh, value);
        }

        public long MiniDailyLow
        {
            get => _miniDailyLow;
            private set => SetField(ref _miniDailyLow, value);
        }

        public long MiniDailyClose
        {
            get => _miniDailyClose;
            private set => SetField(ref _miniDailyClose, value);
        }

        public Brush PriceBrush
        {
            get => _priceBrush;
            set => SetField(ref _priceBrush, value);
        }

        public Brush MiniDailyBrush
        {
            get => _miniDailyBrush;
            set => SetField(ref _miniDailyBrush, value);
        }

        public string MiniDailyMarket
        {
            get => _miniDailyMarket;
            private set => SetField(ref _miniDailyMarket, NormalizeText(value));
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
        public string TodayTradeValueText => TodayTradeValue > 0 ? $"{TodayTradeValue / 100_000_000.0:0.#}억" : string.Empty;
        public bool IsTodayTradeValueStrong => TodayTradeValue >= TradeValueSignalThreshold;
        public string MarketBadgeText => string.IsNullOrWhiteSpace(MarketName) ? MarketTypeCode : NormalizeMarketName(MarketName);
        public string OrderWarningBadgeText => FormatOrderWarning(OrderWarning);
        public string OrderWarningListBadgeText => NormalizeText(OrderWarning) == "5" ? "WARN" : OrderWarningBadgeText;
        public string AuditInfoBadgeText => FormatAuditInfo(AuditInfo);
        public string GateBaseCandleBadgeText
        {
            get
            {
                if (!GateBaseCandleFound || GateBaseCandleOffset < 0)
                    return string.Empty;

                if (IsIntradayPreCandidate)
                    return string.Empty;

                if (GateBaseCandleOffset == 0 &&
                    string.Equals(GateBaseCandleDate, DateTime.Now.ToString("yyyyMMdd"), StringComparison.Ordinal))
                    return "NEW";

                return $"D+{GateBaseCandleOffset}";
            }
        }

        public string GateBaseCandleDetailText
        {
            get
            {
                if (!GateBaseCandleFound)
                    return string.Empty;

                string valueText = GateBaseCandleTradeValue >= 100_000_000
                    ? $"{GateBaseCandleTradeValue / 100_000_000.0:0.#}억"
                    : "-";
                return $"{GateBaseCandleDate} {GateBaseCandleChangeRate:0.##}% {valueText}";
            }
        }

        public string ConditionEntryReasonBadgeText => FormatConditionEntryReasonBadge(ConditionEntryReason);
        public string ConditionEntryReasonDetailText => FormatConditionEntryReasonDetail(ConditionEntryReason);
        public string PreCandidateBadgeText => IsIntradayPreCandidate ? "NEW" : string.Empty;
        public bool MiniDailyHasCandle => MiniDailyOpen > 0 && MiniDailyHigh > 0 && MiniDailyLow > 0 && MiniDailyClose > 0;
        public double MiniDailyWickTop => CalculateMiniDailyY(MiniDailyHigh);
        public double MiniDailyWickHeight => Math.Max(2, CalculateMiniDailyY(MiniDailyLow) - CalculateMiniDailyY(MiniDailyHigh));
        public double MiniDailyBodyTop => Math.Min(CalculateMiniDailyY(MiniDailyOpen), CalculateMiniDailyY(MiniDailyClose));
        public double MiniDailyBodyHeight => Math.Max(3, Math.Abs(CalculateMiniDailyY(MiniDailyOpen) - CalculateMiniDailyY(MiniDailyClose)));

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

        public void ApplyDisplayPrice(long price, string market)
        {
            if (price <= 0)
                return;

            string normalizedMarket = NormalizeMarketCode(market);
            StoreMarketPrice(price, normalizedMarket);

            DisplayPriceMarket = normalizedMarket;
            CurrentPrice = price;
        }

        public void StoreMarketPrice(long price, string market)
        {
            if (price <= 0)
                return;

            string normalizedMarket = NormalizeMarketCode(market);
            if (normalizedMarket == "NXT")
                NxtDisplayPrice = price;
            else
                KrxDisplayPrice = price;
        }

        public bool TryKeepNxtDisplayPrice()
        {
            if (NxtDisplayPrice <= 0)
                return false;

            DisplayPriceMarket = "NXT";
            CurrentPrice = NxtDisplayPrice;
            return true;
        }

        public void WaitForDisplayPrice(string market)
        {
            DisplayPriceMarket = NormalizeMarketCode(market);
            CurrentPrice = 0;
        }

        public void SetMiniDailyCandle(long open, long high, long low, long close, Brush brush, string market = "")
        {
            if (open <= 0 && high <= 0 && low <= 0 && close <= 0)
                return;

            MiniDailyMarket = NormalizeMarketCode(market);
            long safeClose = close > 0 ? close : CurrentPrice;
            if (safeClose <= 0)
                safeClose = open > 0 ? open : high > 0 ? high : low;

            long safeOpen = open > 0 ? open : safeClose;
            long safeHigh = Math.Max(Math.Max(high, safeOpen), safeClose);
            long safeLow = low > 0 ? Math.Min(Math.Min(low, safeOpen), safeClose) : Math.Min(safeOpen, safeClose);

            MiniDailyOpen = safeOpen;
            MiniDailyHigh = safeHigh;
            MiniDailyLow = safeLow;
            MiniDailyClose = safeClose;
            MiniDailyBrush = brush;
            NotifyMiniDailyChanged();
        }

        public void ApplyMiniDailyRealtimePrice(long price, Brush brush, string market = "")
        {
            if (price <= 0)
                return;

            string normalizedMarket = NormalizeMarketCode(market);
            if (!MiniDailyHasCandle ||
                (!string.IsNullOrWhiteSpace(MiniDailyMarket) &&
                 !string.Equals(MiniDailyMarket, normalizedMarket, StringComparison.OrdinalIgnoreCase)))
            {
                SetMiniDailyCandle(price, price, price, price, brush, normalizedMarket);
                return;
            }

            MiniDailyMarket = normalizedMarket;
            MiniDailyHigh = Math.Max(MiniDailyHigh, price);
            MiniDailyLow = MiniDailyLow > 0 ? Math.Min(MiniDailyLow, price) : price;
            MiniDailyClose = price;
            MiniDailyBrush = brush;
            NotifyMiniDailyChanged();
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

        private void NotifyMiniDailyChanged()
        {
            OnPropertyChanged(nameof(MiniDailyHasCandle));
            OnPropertyChanged(nameof(MiniDailyWickTop));
            OnPropertyChanged(nameof(MiniDailyWickHeight));
            OnPropertyChanged(nameof(MiniDailyBodyTop));
            OnPropertyChanged(nameof(MiniDailyBodyHeight));
        }

        private static string NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string NormalizeMarketCode(string? value)
        {
            string text = NormalizeText(value).ToUpperInvariant();
            return text == "NXT" ? "NXT" : "KRX";
        }

        private double CalculateMiniDailyY(long price)
        {
            const double height = 34d;
            if (!MiniDailyHasCandle || price <= 0)
                return height / 2d;

            long high = Math.Max(MiniDailyHigh, Math.Max(MiniDailyOpen, MiniDailyClose));
            long low = MiniDailyLow > 0
                ? Math.Min(MiniDailyLow, Math.Min(MiniDailyOpen, MiniDailyClose))
                : Math.Min(MiniDailyOpen, MiniDailyClose);
            double range = Math.Max(1d, high - low);
            return Math.Max(1d, Math.Min(height - 1d, (high - price) / range * (height - 4d) + 2d));
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

        private static string FormatConditionEntryReasonBadge(string? value)
        {
            return NormalizeText(value).ToUpperInvariant() switch
            {
                "BASE_DONE" => "BASE",
                "FORMING" => "FORM",
                "RANK_HOT" => "RANK",
                _ => string.Empty
            };
        }

        private static string FormatConditionEntryReasonDetail(string? value)
        {
            return NormalizeText(value).ToUpperInvariant() switch
            {
                "BASE_DONE" => "이미 완성된 기준봉 문",
                "FORMING" => "장중 형성 중 문(추정)",
                "RANK_HOT" => "당일 랭킹 강세 문(추정)",
                _ => string.Empty
            };
        }
    }
}
