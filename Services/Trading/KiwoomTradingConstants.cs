namespace TradingDashboard.Services.Trading
{
    public static class KiwoomTradingConstants
    {
        public const string MarketKrx = "KRX";
        public const string MarketNxt = "NXT";
        public const string MarketSor = "SOR";

        public const string IntegratedExchangeType = "0";
        public const string KrxExchangeType = "1";
        public const string NxtExchangeType = "2";

        public const string TradeTypeLimit = "0";
        public const string TradeTypeMarket = "3";
        public const string TradeTypeConditionalLimit = "5";
        public const string TradeTypePreOpenAfterHours = "61";
        public const string TradeTypeAfterHoursSinglePrice = "62";
        public const string TradeTypeAfterClose = "81";
    }
}
