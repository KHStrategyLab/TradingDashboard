using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class DailyBaseCandleVerifier
    {
        public IReadOnlyList<BacktestBaseCandle> Verify(
            BacktestCandidate candidate,
            IEnumerable<BacktestDailyBar> dailyBars,
            int lookbackBars = 120,
            long minTradingValue = 50_000_000_000,
            decimal minChangeRate = 20m)
        {
            List<BacktestDailyBar> bars = [.. (dailyBars ?? [])
                .Where(bar => bar != null && !string.IsNullOrWhiteSpace(bar.Date))
                .OrderBy(bar => bar.Date)
                .TakeLast(Math.Max(1, lookbackBars))];

            List<BacktestBaseCandle> results = [];
            foreach (BacktestDailyBar bar in bars)
            {
                long tradingValue = ResolveTradingValue(bar);
                decimal changeRate = bar.ChangeRate;
                if (changeRate == 0 && bar.PreviousClose > 0)
                    changeRate = (bar.Close - bar.PreviousClose) / (decimal)bar.PreviousClose * 100m;

                if (tradingValue < minTradingValue || changeRate < minChangeRate)
                    continue;

                decimal closeLocation = ResolveCloseLocationPercent(bar);
                decimal upperTail = ResolveUpperTailPercent(bar);
                results.Add(new BacktestBaseCandle
                {
                    Key = BuildBaseCandleKey(candidate.Code, candidate.Market, bar.Date),
                    Code = BacktestDataStore.NormalizeCode(candidate.Code),
                    Name = candidate.Name,
                    Market = BacktestDataStore.NormalizeMarket(candidate.Market),
                    BaseCandleDate = bar.Date,
                    BaseOpen = bar.Open,
                    BaseHigh = bar.High,
                    BaseLow = bar.Low,
                    BaseClose = bar.Close,
                    BaseVolume = bar.Volume,
                    BaseTradingValue = tradingValue,
                    PreviousClose = bar.PreviousClose,
                    ChangeRate = changeRate,
                    CloseLocationPercent = closeLocation,
                    UpperTailPercent = upperTail,
                    Status = string.Equals(bar.Status, "Provisional", StringComparison.OrdinalIgnoreCase)
                        ? "Provisional"
                        : "Verified",
                    SourceName = candidate.SourceName,
                    VerifiedAt = DateTime.Now.ToString("yyyyMMddHHmmss")
                });
            }

            return results;
        }

        public static string BuildBaseCandleKey(string code, string market, string date) =>
            $"{BacktestDataStore.NormalizeCode(code)}|{BacktestDataStore.NormalizeMarket(market)}|{BacktestDataStore.NormalizeDate(date)}";

        private static long ResolveTradingValue(BacktestDailyBar bar)
        {
            long estimated = bar.Close > 0 && bar.Volume > 0
                ? (long)Math.Min(long.MaxValue, bar.Close * (double)bar.Volume)
                : 0;
            if (bar.TradingValue <= 0)
                return estimated;

            long storedAsMillionWon = bar.TradingValue > long.MaxValue / 1_000_000
                ? long.MaxValue
                : bar.TradingValue * 1_000_000;

            if (estimated <= 0)
                return Math.Max(bar.TradingValue, storedAsMillionWon);

            long rawDistance = Math.Abs(bar.TradingValue - estimated);
            long millionWonDistance = Math.Abs(storedAsMillionWon - estimated);
            return millionWonDistance <= rawDistance ? storedAsMillionWon : Math.Max(bar.TradingValue, estimated);
        }

        private static decimal ResolveCloseLocationPercent(BacktestDailyBar bar)
        {
            long range = bar.High - bar.Low;
            if (range <= 0)
                return 0;
            return (bar.Close - bar.Low) / (decimal)range * 100m;
        }

        private static decimal ResolveUpperTailPercent(BacktestDailyBar bar)
        {
            long range = bar.High - bar.Low;
            if (range <= 0)
                return 0;
            long bodyHigh = Math.Max(bar.Open, bar.Close);
            return (bar.High - bodyHigh) / (decimal)range * 100m;
        }
    }
}
