using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;
using TradingDashboard.Services.Backtests;

namespace TradingDashboard.Services
{
    public sealed class LeaderHistoryCandidatePromotionJob
    {
        private readonly CandidateLedgerStore _candidateStore;
        private readonly LeaderHistoryStore _leaderStore;
        private readonly LeaderHistoryQualityScorer _scorer;

        public LeaderHistoryCandidatePromotionJob(
            CandidateLedgerStore? candidateStore = null,
            LeaderHistoryStore? leaderStore = null,
            LeaderHistoryQualityScorer? scorer = null)
        {
            _candidateStore = candidateStore ?? new CandidateLedgerStore();
            _leaderStore = leaderStore ?? new LeaderHistoryStore();
            _scorer = scorer ?? new LeaderHistoryQualityScorer();
        }

        public LeaderHistoryRebuildSummary Promote(int lookbackTradingDays = 6)
        {
            int resolvedLookback = Math.Max(1, lookbackTradingDays);
            string runId = DateTime.Now.ToString("yyyyMMddHHmmss");
            List<CandidateLedgerEntry> candidates = [.. _candidateStore.LoadActive()
                .Where(item => item != null &&
                    !string.IsNullOrWhiteSpace(item.Code) &&
                    !string.IsNullOrWhiteSpace(item.CandidateDate))];
            List<string> recentDates = [.. candidates
                .Select(item => BacktestDataStore.NormalizeDate(item.CandidateDate))
                .Where(date => !string.IsNullOrWhiteSpace(date))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(date => date)
                .TakeLast(resolvedLookback)];

            string startDate = recentDates.FirstOrDefault() ?? string.Empty;
            string endDate = recentDates.LastOrDefault() ?? string.Empty;
            List<LeaderHistoryEntry> leaders = [.. candidates
                .Where(item => recentDates.Contains(BacktestDataStore.NormalizeDate(item.CandidateDate), StringComparer.Ordinal))
                .Where(IsPromotable)
                .Select(item => _scorer.Score(BuildLeader(item, runId, resolvedLookback)))
                .Where(item => !item.ManualDiscarded)
                .OrderByDescending(item => item.QualityScore)
                .ThenByDescending(item => item.TradingValue)
                .ThenBy(item => item.Code)];

            _leaderStore.SaveActive(leaders);
            _leaderStore.SaveArchiveSnapshot(leaders, runId);

            var summary = new LeaderHistoryRebuildSummary
            {
                RunId = runId,
                LookbackTradingDays = resolvedLookback,
                StartDate = startDate,
                EndDate = endDate,
                DailyBarCount = candidates.Count,
                CandidateCount = candidates.Count,
                ActiveLeaderCount = leaders.Count,
                GradeCounts = leaders
                    .GroupBy(item => item.QualityGrade, StringComparer.Ordinal)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                LeaderTypeCounts = leaders
                    .GroupBy(item => item.LeaderType, StringComparer.Ordinal)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                Logs =
                [
                    $"leader history promoted from candidate ledger: {leaders.Count}leaders / {startDate}-{endDate}",
                    $"candidate rows: {candidates.Count}",
                    $"active path: {_leaderStore.ActivePath}",
                    "scores are calculated only at LeaderHistory promotion stage"
                ]
            };
            _leaderStore.SaveSummary(summary);
            return summary;
        }

        private static bool IsPromotable(CandidateLedgerEntry candidate)
        {
            string market = BacktestDataStore.NormalizeMarket(candidate.Market);
            if (!string.Equals(market, "KRX", StringComparison.Ordinal))
                return false;

            long tradingValue = Math.Max(candidate.TradingValue, candidate.TodayTradingValue);
            return tradingValue >= 100_000_000_000 &&
                candidate.ChangeRate >= 25m &&
                candidate.TodayClose > 0;
        }

        private static LeaderHistoryEntry BuildLeader(CandidateLedgerEntry candidate, string runId, int lookbackTradingDays)
        {
            string code = BacktestDataStore.NormalizeCode(candidate.Code);
            string market = BacktestDataStore.NormalizeMarket(candidate.Market);
            string baseDate = BacktestDataStore.NormalizeDate(candidate.CandidateDate);
            long tradingValue = Math.Max(candidate.TradingValue, candidate.TodayTradingValue);
            return new LeaderHistoryEntry
            {
                Key = $"{code}|{market}|{baseDate}",
                Code = code,
                Name = string.IsNullOrWhiteSpace(candidate.Name) ? code : candidate.Name,
                Market = market,
                BaseDate = baseDate,
                Open = candidate.TodayOpen,
                High = candidate.TodayHigh,
                Low = candidate.TodayLow,
                Close = candidate.TodayClose,
                Volume = candidate.TodayVolume,
                TradingValue = tradingValue,
                ChangeRate = candidate.ChangeRate,
                DailyRsi14 = candidate.DailyRsi14,
                CloseLocationPercent = candidate.CloseLocationPercent ?? 0m,
                UpperTailPercent = candidate.UpperTailPercent ?? 0m,
                MarketCap = candidate.MarketCap,
                MarketCapClass = candidate.MarketCapClass,
                ValueToMarketCapPercent = candidate.ValueToMarketCapPercent,
                TradingValueToMarketCapPercent = candidate.TradingValueToMarketCapPercent ?? candidate.ValueToMarketCapPercent,
                TurnoverRate = candidate.TurnoverRateByFloatingShares ?? candidate.TurnoverRateByListedShares,
                KrxClose = candidate.TodayClose,
                BollingerUpperBreak = candidate.IsBollingerUpperBreak == true,
                PrevHighPlus10 = candidate.PrevHigh > 0 && candidate.TodayClose >= candidate.PrevHigh * 1.10m,
                Source = "CandidateLedgerPromotion",
                Status = string.Equals(candidate.TodayBarStatus, "Provisional", StringComparison.OrdinalIgnoreCase)
                    ? "Provisional"
                    : "Active",
                SavedAt = runId,
                ExpiresAfterTradingDays = lookbackTradingDays
            };
        }
    }
}
