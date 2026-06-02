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
            score += ScoreMarketCapScale(entry, reasons);
            score += ScoreMarketRelativePower(entry, reasons);
            score += ScoreTurnover(entry, reasons);
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
            entry.MarketCapClass = ResolveMarketCapClass(entry.MarketCap);
            entry.QualityReason = string.Join(" / ", reasons);
            return entry;
        }

        private static decimal ScoreTradingValue(long tradingValue, List<string> reasons)
        {
            decimal valueB = tradingValue / 100_000_000m;
            if (valueB >= 10_000m)
            {
                reasons.Add("trading value 1T+");
                return 22m;
            }
            if (valueB >= 5_000m)
            {
                reasons.Add("trading value 500B+");
                return 19m;
            }
            if (valueB >= 3_000m)
            {
                reasons.Add("trading value 300B+");
                return 16m;
            }
            if (valueB >= 2_000m)
            {
                reasons.Add("trading value 200B+");
                return 13m;
            }
            if (valueB >= 1_000m)
            {
                reasons.Add("trading value 100B+");
                return 9m;
            }

            return 0m;
        }

        private static decimal ScoreMarketCapScale(LeaderHistoryEntry entry, List<string> reasons)
        {
            if (entry.MarketCap is not decimal marketCap || marketCap <= 0)
                return 0m;

            decimal capB = marketCap / 100_000_000m;
            if (capB >= 100_000m)
            {
                reasons.Add("market cap 10T+");
                return 8m;
            }
            if (capB >= 30_000m)
            {
                reasons.Add("market cap 3T+");
                return 6m;
            }
            if (capB >= 10_000m)
            {
                reasons.Add("market cap 1T+");
                return 4m;
            }
            if (capB >= 3_000m)
                return 2m;

            return 0m;
        }

        private static decimal ScoreMarketRelativePower(LeaderHistoryEntry entry, List<string> reasons)
        {
            decimal? marketRelativePower = entry.TradingValueToMarketCapPercent ?? entry.ValueToMarketCapPercent;
            if (marketRelativePower is not decimal ratio)
            {
                reasons.Add("market cap missing");
                return 10m;
            }

            if (ratio >= 50m)
            {
                reasons.Add("value/cap 50%+");
                return 18m;
            }
            if (ratio >= 30m)
            {
                reasons.Add("value/cap 30%+");
                return 15m;
            }
            if (ratio >= 20m)
            {
                reasons.Add("value/cap 20%+");
                return 12m;
            }
            if (ratio >= 10m)
            {
                reasons.Add("value/cap 10%+");
                return 9m;
            }
            if (ratio >= 5m)
                return 4m;

            return 0m;
        }

        private static decimal ScoreTurnover(LeaderHistoryEntry entry, List<string> reasons)
        {
            if (entry.TurnoverRate is not decimal turnover || turnover <= 0)
                return 0m;

            if (turnover >= 50m)
            {
                reasons.Add("turnover 50%+");
                return 7m;
            }
            if (turnover >= 30m)
            {
                reasons.Add("turnover 30%+");
                return 6m;
            }
            if (turnover >= 15m)
            {
                reasons.Add("turnover 15%+");
                return 4m;
            }
            if (turnover >= 8m)
                return 2m;

            return 0m;
        }

        private static decimal ScoreChangeRate(decimal changeRate, List<string> reasons)
        {
            if (changeRate >= 29.5m)
            {
                reasons.Add("limit-up zone");
                return 12m;
            }
            if (changeRate >= 25m)
            {
                reasons.Add("change 25%+");
                return 10m;
            }
            if (changeRate >= 20m)
                return 7m;

            return 0m;
        }

        private static decimal ScorePrevHigh(bool prevHighPlus10, List<string> reasons)
        {
            if (!prevHighPlus10)
                return 0m;

            reasons.Add("prev high +10%");
            return 12m;
        }

        private static decimal ScoreCloseLocation(decimal closeLocationPercent, List<string> reasons)
        {
            if (closeLocationPercent >= 95m)
            {
                reasons.Add("close near high 95%+");
                return 8m;
            }
            if (closeLocationPercent >= 90m)
                return 6m;
            if (closeLocationPercent >= 80m)
                return 5m;
            if (closeLocationPercent >= 70m)
                return 3m;

            return 0m;
        }

        private static decimal ScoreUpperTail(decimal upperTailPercent, List<string> reasons)
        {
            if (upperTailPercent <= 5m)
            {
                reasons.Add("short upper tail <=5%");
                return 8m;
            }
            if (upperTailPercent <= 10m)
                return 6m;
            if (upperTailPercent <= 15m)
                return 4m;
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
                decimal? marketRelativePower = entry.TradingValueToMarketCapPercent ?? entry.ValueToMarketCapPercent;
                if (capB >= 100_000m && valueB >= 10_000m)
                    return "Market Leader";
                if (marketRelativePower >= 50m && capB < 3_000m)
                    return "Speculative Leader";
                if (marketRelativePower >= 20m)
                    return "Stock Leader";
            }

            if (valueB >= 10_000m)
                return "Market Leader Candidate";
            if (valueB >= 3_000m)
                return "Stock Leader Candidate";
            return "Leader Candidate";
        }

        private static string ResolveMarketCapClass(decimal? marketCap)
        {
            if (marketCap is not decimal cap || cap <= 0)
                return "Unknown";

            decimal capB = cap / 100_000_000m;
            if (capB >= 100_000m)
                return "MegaCap";
            if (capB >= 30_000m)
                return "LargeCap";
            if (capB >= 10_000m)
                return "MidLargeCap";
            if (capB >= 3_000m)
                return "MidCap";
            return "SmallCap";
        }
    }
}
