namespace TradingDashboard.Models
{
    public class StockStatusMetrics
    {
        public string OpenPriceText { get; set; } = "-";
        public string HighPriceText { get; set; } = "-";
        public string LowPriceText { get; set; } = "-";
        public string ClosePriceText { get; set; } = "-";
        public string BasePriceText { get; set; } = "-";
        public string VolumeText { get; set; } = "-";
        public string TradingValueText { get; set; } = "-";
        public string MarketCapText { get; set; } = "-";
        public string ListedSharesText { get; set; } = "-";
        public string FloatRatioText { get; set; } = "-";
        public string TurnoverRateText { get; set; } = "-";
        public string ChangeRateText { get; set; } = "-";
        public string PrevDiffText { get; set; } = "-";
        public string VolumeRatioText { get; set; } = "-";
        public long BuyExecCum { get; set; }
        public long SellExecCum { get; set; }
        public long DailyTradeQty { get; set; }
        public long DailyTradeValueMillion { get; set; }
        public long BeforeMarketTradeQty { get; set; }
        public long RegularMarketTradeQty { get; set; }
        public long AfterMarketTradeQty { get; set; }
        public long DailySectionTradeQty { get; set; }
        public string ProgramBuyText { get; set; } = "-";
        public long ProgramNetQuantity { get; set; }
        public bool HasProgramTrade { get; set; }
    }
}
