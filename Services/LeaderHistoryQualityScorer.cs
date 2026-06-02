using System;
using System.Collections.Generic;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class LeaderHistoryQualityScorer
    {
        public LeaderHistoryEntry Score(LeaderHistoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            decimal score = 0m;
            List<string> reasons = [];

            score += ScoreTradingValue(entry.TradingValue, reasons);
            score += ScoreMarketRelativePower(entry, reasons);
            score += ScoreChangeRate(entry.ChangeRate, reasons);
            score += ScorePrevHigh(entry.PrevHighPlus10, reasons);
            score += ScoreCloseLocation(entry.CloseLocationPercent, reasons);
            score += ScoreUpperTail(entry.UpperTailPercent, reasons);
            if (entry.BollingerUpperBreak)
            {
                score += 5m;
                reasons.Add("bollinger upper break");
            }

            entry.QualityScore = Math.Min(100m, Math.Round(score, 2));
            entry.QualityGrade = ResolveGrade(entry.QualityScore);
            entry.LeaderType = ResolveLeaderType(entry);
            entry.QualityReason = string.Join(" / ", reasons);
            return entry;
        }

        private static decimal ScoreTradingValue(long tradingValue, List<string> reasons)
        {
            decimal valueB = tradingValue / 100_000_000m;
            if (valueB >= 10_000m)
            {
                reasons.Add("trading value 1T+");
                return 25m;
            }
            if (valueB >= 5_000m)
            {
                reasons.Add("trading value 500B+");
                return 22m;
            }
            if (valueB >= 3_000m)
            {
                reasons.Add("trading value 300B+");
                return 18m;
            }
            if (valueB >= 2_000m)
            {
                reasons.Add("trading value 200B+");
                return 15m;
            }
            if (valueB >= 1_000m)
            {
                reasons.Add("trading value 100B+");
                return 10m;
            }

            return 0m;
        }

        private static decimal ScoreMarketRelativePower(LeaderHistoryEntry entry, List<string> reasons)
        {
            if (entry.ValueToMarketCapPercent is not decimal ratio)
            {
                reasons.Add("market cap missing");
                return 10m;
            }

            if (ratio >= 50m)
            {
                reasons.Add("value/cap 50%+");
                return 20m;
            }
            if (ratio >= 30m)
            {
                reasons.Add("value/cap 30%+");
                return 17m;
            }
            if (ratio >= 20m)
            {
                reasons.Add("value/cap 20%+");
                return 14m;
            }
            if (ratio >= 10m)
            {
                reasons.Add("value/cap 10%+");
                return 10m;
            }
            if (ratio >= 5m)
                return 5m;

            return 0m;
        }

        private static decimal ScoreChangeRate(decimal changeRate, List<string> reasons)
        {
            if (changeRate >= 29.5m)
            {
                reasons.Add("limit-up zone");
                return 15m;
            }
            if (changeRate >= 25m)
            {
                reasons.Add("change 25%+");
                return 12m;
            }
            if (changeRate >= 20m)
                return 8m;

            return 0m;
        }

        private static decimal ScorePrevHigh(bool prevHighPlus10, List<string> reasons)
        {
            if (!prevHighPlus10)
                return 0m;

            reasons.Add("prev high +10%");
            return 15m;
        }

        private static decimal ScoreCloseLocation(decimal closeLocationPercent, List<string> reasons)
        {
            if (closeLocationPercent >= 95m)
            {
                reasons.Add("close near high 95%+");
                return 10m;
            }
            if (closeLocationPercent >= 90m)
                return 8m;
            if (closeLocationPercent >= 80m)
                return 6m;
            if (closeLocationPercent >= 70m)
                return 3m;

            return 0m;
        }

        private static decimal ScoreUpperTail(decimal upperTailPercent, List<string> reasons)
        {
            if (upperTailPercent <= 5m)
            {
                reasons.Add("short upper tail <=5%");
                return 10m;
            }
            if (upperTailPercent <= 10m)
                return 8m;
            if (upperTailPercent <= 15m)
                return 5m;
            if (upperTailPercent <= 20m)
                return 2m;

            return 0m;
        }

        private static string ResolveGrade(decimal score)
        {
            if (score >= 90m)
                return "A++";
            if (score >= 80m)
                return "A+";
            if (score >= 70m)
                return "A";
            if (score >= 60m)
                return "B";
            if (score >= 50m)
                return "C";
            return "Exclude";
        }

        private static string ResolveLeaderType(LeaderHistoryEntry entry)
        {
            decimal valueB = entry.TradingValue / 100_000_000m;
            if (entry.MarketCap is decimal cap)
            {
                decimal capB = cap / 100_000_000m;
                if (capB >= 100_000m && valueB >= 10_000m)
                    return "Market Leader";
                if (entry.ValueToMarketCapPercent >= 50m && capB < 3_000m)
                    return "Speculative Leader";
                if (entry.ValueToMarketCapPercent >= 20m)
                    return "Stock Leader";
            }

            if (valueB >= 10_000m)
                return "Market Leader Candidate";
            if (valueB >= 3_000m)
                return "Stock Leader Candidate";
            return "Leader Candidate";
        }
    }
}
