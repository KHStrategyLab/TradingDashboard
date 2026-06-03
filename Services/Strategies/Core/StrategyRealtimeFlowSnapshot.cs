using System;

namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyRealtimeFlowSnapshot(
        string Code,
        string Market,
        DateTime LastTickAt,
        long LastPrice,
        long LastTradeQuantity,
        bool LastTradeIsBuy,
        long BuyTradeVolume60s,
        long SellTradeVolume60s,
        long BuyTradeValue60s,
        long SellTradeValue60s,
        int BuyTradeCount60s,
        int SellTradeCount60s,
        DateTime LastOrderBookAt,
        long BestAskPrice,
        long BestBidPrice,
        long TotalAskQuantity,
        long TotalBidQuantity)
    {
        public static StrategyRealtimeFlowSnapshot Empty { get; } = new(
            string.Empty,
            string.Empty,
            DateTime.MinValue,
            0,
            0,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            DateTime.MinValue,
            0,
            0,
            0,
            0);

        public long TotalTradeVolume60s => BuyTradeVolume60s + SellTradeVolume60s;

        public long TotalTradeValue60s => BuyTradeValue60s + SellTradeValue60s;

        public double BuyTradeVolumeRatio60s =>
            TotalTradeVolume60s > 0 ? BuyTradeVolume60s / (double)TotalTradeVolume60s * 100d : 0d;

        public double BidQuantityRatio =>
            TotalAskQuantity + TotalBidQuantity > 0
                ? TotalBidQuantity / (double)(TotalAskQuantity + TotalBidQuantity) * 100d
                : 0d;

        public bool HasFreshTick(DateTime now, double maxAgeSeconds = 10d) =>
            LastTickAt != DateTime.MinValue &&
            (now - LastTickAt).TotalSeconds <= maxAgeSeconds;

        public bool HasFreshOrderBook(DateTime now, double maxAgeSeconds = 15d) =>
            LastOrderBookAt != DateTime.MinValue &&
            (now - LastOrderBookAt).TotalSeconds <= maxAgeSeconds;

        public StrategyRealtimeFlowSnapshot WithOrderBook(
            DateTime at,
            long bestAskPrice,
            long bestBidPrice,
            long totalAskQuantity,
            long totalBidQuantity) =>
            this with
            {
                LastOrderBookAt = at,
                BestAskPrice = bestAskPrice,
                BestBidPrice = bestBidPrice,
                TotalAskQuantity = totalAskQuantity,
                TotalBidQuantity = totalBidQuantity
            };
    }
}
