using System;

namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyRealtimeTradeSample(
        DateTime At,
        long Price,
        long Quantity,
        long TradeValue,
        bool IsBuy);
}
