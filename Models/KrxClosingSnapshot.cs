using System.Collections.Generic;

namespace TradingDashboard.Models
{
    public class KrxClosingSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public long CurrentPrice { get; set; }
        public long BasePrice { get; set; }
        public long DayChange { get; set; }
        public string ChangeRateText { get; set; } = "-";
        public long BuyExecCum { get; set; }
        public long SellExecCum { get; set; }
        public long DailyTradeQty { get; set; }
        public long DailyTradeValueMillion { get; set; }
        public long BeforeMarketTradeQty { get; set; }
        public long RegularMarketTradeQty { get; set; }
        public long AfterMarketTradeQty { get; set; }
        public long DailySectionTradeQty { get; set; }
        public List<HogaQuoteLevel> SellLevels { get; } = [];
        public List<HogaQuoteLevel> BuyLevels { get; } = [];
        public List<ClosingTradePrint> RecentTrades { get; } = [];
    }

    public class HogaQuoteLevel
    {
        public long Price { get; set; }
        public long Quantity { get; set; }
    }

    public class ClosingTradePrint
    {
        public long Price { get; set; }
        public long Quantity { get; set; }
        public bool IsBuyAggressive { get; set; }
    }
}
