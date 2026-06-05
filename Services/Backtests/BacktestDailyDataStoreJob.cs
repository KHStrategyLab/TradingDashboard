using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestDailyDataStoreJob
    {
        private readonly BacktestConditionCandidateLoader _candidateLoader;
        private readonly BacktestDataStore _dataStore;
        private readonly BacktestDatasetBuilder _datasetBuilder;
        private readonly BacktestSettings _settings;

        public BacktestDailyDataStoreJob(
            KiwoomRestConditionService kiwoomService,
            BacktestSettings? settings = null,
            BacktestDataStore? dataStore = null,
            DailyBaseCandleVerifier? baseCandleVerifier = null)
        {
            _settings = settings ?? new BacktestSettings();
            _dataStore = dataStore ?? new BacktestDataStore();
            _candidateLoader = new BacktestConditionCandidateLoader(kiwoomService, _settings);
            _datasetBuilder = new BacktestDatasetBuilder(kiwoomService, _dataStore, baseCandleVerifier);
        }

        public async Task<BacktestDatasetUpdateSummary> RunAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<BacktestCandidate> candidates = await _candidateLoader
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);

            _dataStore.SaveCandidates(candidates, _settings.CandidateSourceName);

            List<BacktestCandidate> downloadCandidates = [.. candidates];
            if (BacktestMarketModeHelper.IsAlIntegrated(_settings))
            {
                downloadCandidates.AddRange(candidates
                    .Where(item => string.Equals(BacktestDataStore.NormalizeMarket(item.Market), "KRX", StringComparison.Ordinal))
                    .Select(item => new BacktestCandidate
                    {
                        Code = BacktestDataStore.NormalizeCode(item.Code),
                        Name = item.Name,
                        Market = "AL",
                        CandidateDate = BacktestDataStore.NormalizeDate(item.CandidateDate),
                        NxtEnabled = item.NxtEnabled,
                        StrategyCode = item.StrategyCode,
                        Source = $"{item.Source}_AL",
                        ImportedAt = item.ImportedAt,
                        Memo = "SOR ON integrated daily data candidate; KRX base-price/gate fields stay separate and are not overwritten"
                    }));
            }

            BacktestDatasetUpdateSummary summary = await _datasetBuilder
                .BuildDailyDataStoreAsync(
                    downloadCandidates,
                    mirrorKrxBaseCandlesForNxt: BacktestMarketModeHelper.IsSorMixed(_settings),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            int nxtEligibleCount = candidates
                .Where(item => item.NxtEnabled)
                .Select(item => BacktestDataStore.NormalizeCode(item.Code))
                .Distinct(StringComparer.Ordinal)
                .Count();
            int krxCandidateCount = candidates.Count(item => string.Equals(BacktestDataStore.NormalizeMarket(item.Market), "KRX", StringComparison.Ordinal));
            int nxtCandidateCount = candidates.Count(item => string.Equals(BacktestDataStore.NormalizeMarket(item.Market), "NXT", StringComparison.Ordinal));
            int alCandidateCount = downloadCandidates.Count(item => string.Equals(BacktestDataStore.NormalizeMarket(item.Market), "AL", StringComparison.Ordinal));
            summary.MarketMode = BacktestMarketModeHelper.NormalizeMarketMode(_settings);
            summary.Logs.Insert(0, $"condition candidates loaded: {_settings.CandidateConditionIndex} / {candidates.Count}stocks / NXT eligible {nxtEligibleCount} / mode {_settings.MarketMode}");
            summary.Logs.Insert(1, $"condition candidates by market: KRX {krxCandidateCount} / NXT {nxtCandidateCount} / AL download {alCandidateCount}");
            return summary;
        }
    }
}
