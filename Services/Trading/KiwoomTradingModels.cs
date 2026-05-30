using System;
using System.Collections.Generic;

namespace TradingDashboard.Services.Trading
{
    public sealed record KiwoomOrderRequest(
        string Market,
        string StockCode,
        long Quantity,
        long OrderPrice,
        string TradeType,
        string ConditionPrice = "")
    {
        public static KiwoomOrderRequest SorMarket(string stockCode, long quantity) =>
            new(
                KiwoomTradingConstants.MarketSor,
                stockCode,
                quantity,
                0,
                KiwoomTradingConstants.TradeTypeMarket);
    }

    public sealed record KiwoomModifyOrderRequest(
        string Market,
        string OriginalOrderNo,
        string StockCode,
        long Quantity,
        long OrderPrice,
        string ConditionPrice = "");

    public sealed record KiwoomCancelOrderRequest(
        string Market,
        string OriginalOrderNo,
        string StockCode,
        long Quantity);

    public sealed record KiwoomOrderResult(
        bool Success,
        string ApiId,
        string OrderNo,
        string RoutedMarket,
        int ReturnCode,
        string ReturnMessage,
        string RawBody)
    {
        public static KiwoomOrderResult Failed(string apiId, int returnCode, string message, string rawBody = "") =>
            new(false, apiId, string.Empty, string.Empty, returnCode, message, rawBody);
    }

    public sealed record KiwoomOpenOrder(
        string OrderNo,
        string StockCode,
        string StockName,
        string OrderStatus,
        string OrderSideText,
        string TradeTypeText,
        long OrderQuantity,
        long UnfilledQuantity,
        long FilledQuantity,
        long OrderPrice,
        long FilledPrice,
        string ExchangeType,
        string ExchangeText,
        bool IsSor);

    public sealed record KiwoomFill(
        string OrderNo,
        string StockCode,
        string StockName,
        string OrderStatus,
        string OrderSideText,
        string TradeTypeText,
        long OrderQuantity,
        long FilledQuantity,
        long UnfilledQuantity,
        long OrderPrice,
        long FilledPrice,
        string OrderTime,
        string ExchangeType,
        string ExchangeText,
        bool IsSor);

    public sealed record KiwoomHolding(
        string StockCode,
        string StockName,
        long HoldingQuantity,
        long OrderableQuantity,
        long CurrentPrice,
        long AverageBuyPrice,
        long PurchaseAmount,
        long EvaluationAmount,
        long EvaluationProfit,
        decimal ProfitRate);

    public sealed record KiwoomBalanceSnapshot(
        string SourceApi,
        string QueryMarket,
        DateTime CapturedAt,
        long TotalPurchaseAmount,
        long TotalEvaluationAmount,
        long TotalEvaluationProfit,
        decimal TotalProfitRate,
        IReadOnlyList<KiwoomHolding> Holdings,
        string RawBody);

    public sealed record KiwoomRealizedProfitSnapshot(
        DateTime QueryDate,
        long RealizedProfit,
        long TradeCommission,
        long TradeTax,
        string RawBody);
}
