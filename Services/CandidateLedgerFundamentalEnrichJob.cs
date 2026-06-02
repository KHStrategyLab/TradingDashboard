using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;
using TradingDashboard.Services.Backtests;

namespace TradingDashboard.Services
{
    public sealed class CandidateLedgerFundamentalEnrichJob
    {
        private readonly KiwoomRestConditionService _kiwoomService;
        private readonly CandidateLedgerStore _store;

        public CandidateLedgerFundamentalEnrichJob(
            KiwoomRestConditionService kiwoomService,
            CandidateLedgerStore? store = null)
        {
            _kiwoomService = kiwoomService ?? throw new ArgumentNullException(nameof(kiwoomService));
            _store = store ?? new CandidateLedgerStore();
        }

        public async Task<CandidateLedgerEnrichSummary> EnrichAsync(CancellationToken cancellationToken = default)
        {
            string runId = DateTime.Now.ToString("yyyyMMddHHmmss");
            List<CandidateLedgerEntry> candidates = [.. _store.LoadActive()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .OrderByDescending(item => item.CandidateDate)
                .ThenByDescending(item => item.TradingValue)
                .ThenBy(item => item.Code)];
            var summary = new CandidateLedgerEnrichSummary
            {
                RunId = runId,
                CandidateCount = candidates.Count
            };

            foreach (CandidateLedgerEntry candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string code = BacktestDataStore.NormalizeCode(candidate.Code);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                try
                {
                    StockStatusMetrics metrics = await _kiwoomService
                        .GetStockStatusMetricsByGuideAsync(code, useNxtMarket: false, cancellationToken)
                        .ConfigureAwait(false);

                    if (metrics.ClosePrice <= 0 && metrics.MarketCap <= 0 && metrics.ListedShares <= 0)
                    {
                        MarkFailed(candidate, runId, "fundamental source returned empty metrics");
                        summary.FailedCount++;
                        continue;
                    }

                    ApplyMetrics(candidate, metrics, runId);
                    summary.EnrichedCount++;
                    summary.Logs.Add($"candidate fundamental enriched: {candidate.Code} / marketCap {candidate.MarketCap:0} / listed {candidate.ListedShares:0} / floating {candidate.FloatingShares:0}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    MarkFailed(candidate, runId, $"{ex.GetType().Name}: {ex.Message}");
                    summary.FailedCount++;
                    summary.Logs.Add($"candidate fundamental enrich failed: {candidate.Code} / {ex.Message}");
                }

                try
                {
                    InvestorNetBuyMetrics investorMetrics = await _kiwoomService
                        .GetInvestorNetBuyMetricsAsync(code, candidate.CandidateDate, cancellationToken)
                        .ConfigureAwait(false);

                    ApplyInvestorNetBuy(candidate, investorMetrics, runId);
                    summary.Logs.Add($"candidate investor net buy loaded: {candidate.Code} / foreign {candidate.ForeignNetBuyQuantity:0} / institution {candidate.InstitutionNetBuyQuantity:0} / double {candidate.IsForeignInstitutionDoubleNetBuy}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    candidate.InvestorNetBuyStatus = "Failed";
                    candidate.DataErrorMemo = AppendMemo(candidate.DataErrorMemo, $"InvestorNetBuy {ex.GetType().Name}: {ex.Message}");
                    candidate.UpdatedAt = runId;
                    summary.Logs.Add($"candidate investor net buy failed: {candidate.Code} / {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
            }

            summary.PendingCount = candidates.Count(item => string.Equals(item.FundamentalStatus, "Pending", StringComparison.OrdinalIgnoreCase));
            summary.FundamentalStatusCounts = candidates
                .GroupBy(item => string.IsNullOrWhiteSpace(item.FundamentalStatus) ? "Pending" : item.FundamentalStatus, StringComparer.Ordinal)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            _store.SaveActive(candidates);
            _store.SaveEnrichSummary(summary);
            return summary;
        }

        private static void ApplyMetrics(CandidateLedgerEntry candidate, StockStatusMetrics metrics, string runId)
        {
            long closePrice = metrics.ClosePrice > 0 ? metrics.ClosePrice : candidate.TodayClose;
            long listedShares = metrics.ListedShares;
            long floatingShares = metrics.FloatingShares;
            long marketCap = metrics.MarketCap;
            if (marketCap <= 0 && closePrice > 0 && listedShares > 0)
                marketCap = closePrice * listedShares;

            candidate.MarketCap = marketCap > 0 ? marketCap : null;
            candidate.MarketCapClass = ResolveMarketCapClass(candidate.MarketCap);
            candidate.ListedShares = listedShares > 0 ? listedShares : null;
            candidate.FloatingShares = floatingShares > 0 ? floatingShares : null;
            candidate.FloatingSharesUnit = "share";
            candidate.MarketCapSource = metrics.MarketCapFromApi ? "ka10095.mac" : "calculated:price*listedShares";
            candidate.ListedSharesSource = listedShares > 0 ? "ka10100.listCount" : string.Empty;
            candidate.FloatingSharesSource = floatingShares > 0 ? "ka10007.flo_stkcnt" : string.Empty;
            candidate.FundamentalUpdatedAt = runId;
            candidate.FundamentalStatus = marketCap > 0 || listedShares > 0 || floatingShares > 0 ? "Loaded" : "Pending";

            if (candidate.TradingValue > 0 && marketCap > 0)
            {
                decimal ratio = candidate.TradingValue / (decimal)marketCap * 100m;
                candidate.ValueToMarketCapPercent = ratio;
                candidate.TradingValueToMarketCapPercent = ratio;
            }
            if (candidate.Volume > 0 && listedShares > 0)
                candidate.TurnoverRateByListedShares = candidate.Volume / (decimal)listedShares * 100m;
            if (candidate.Volume > 0 && floatingShares > 0)
                candidate.TurnoverRateByFloatingShares = candidate.Volume / (decimal)floatingShares * 100m;

            candidate.MissingFieldMemo = BuildMissingFieldMemo(candidate);
            candidate.UpdatedAt = runId;
        }

        private static void ApplyInvestorNetBuy(CandidateLedgerEntry candidate, InvestorNetBuyMetrics metrics, string runId)
        {
            if (metrics.Found)
            {
                candidate.ForeignNetBuyQuantity = metrics.ForeignNetBuyQuantity;
                candidate.InstitutionNetBuyQuantity = metrics.InstitutionNetBuyQuantity;
                candidate.IsForeignInstitutionDoubleNetBuy = metrics.IsForeignInstitutionDoubleNetBuy;
                candidate.InvestorNetBuySource = $"{metrics.Source}:{metrics.Unit}";
                candidate.InvestorNetBuyStatus = "Loaded";
            }
            else
            {
                candidate.ForeignNetBuyQuantity = null;
                candidate.InstitutionNetBuyQuantity = null;
                candidate.IsForeignInstitutionDoubleNetBuy = null;
                candidate.InvestorNetBuySource = "ka10059:share";
                candidate.InvestorNetBuyStatus = "Empty";
            }

            candidate.InvestorNetBuyUpdatedAt = runId;
            candidate.UpdatedAt = runId;
        }

        private static void MarkFailed(CandidateLedgerEntry candidate, string runId, string message)
        {
            candidate.FundamentalStatus = "Failed";
            candidate.DataErrorMemo = message;
            candidate.MissingFieldMemo = BuildMissingFieldMemo(candidate);
            candidate.UpdatedAt = runId;
        }

        private static string BuildMissingFieldMemo(CandidateLedgerEntry candidate)
        {
            List<string> missing = [];
            if (candidate.MarketCap is null or <= 0)
                missing.Add("MarketCap");
            if (candidate.ListedShares is null or <= 0)
                missing.Add("ListedShares");
            if (candidate.FloatingShares is null or <= 0)
                missing.Add("FloatingShares");
            return string.Join(";", missing);
        }

        private static string AppendMemo(string current, string message)
        {
            if (string.IsNullOrWhiteSpace(current))
                return message;
            if (string.IsNullOrWhiteSpace(message))
                return current;
            return $"{current}; {message}";
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
