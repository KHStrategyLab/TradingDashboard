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

            BacktestDatasetUpdateSummary summary = await _datasetBuilder
                .BuildDailyDataStoreAsync(
                    candidates,
                    mirrorKrxBaseCandlesForNxt: BacktestMarketModeHelper.IsSorMixed(_settings),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            int nxtEligibleCount = candidates.Count(item => item.NxtEnabled);
            summary.Logs.Insert(0, $"condition candidates loaded: {_settings.CandidateConditionIndex} / {candidates.Count}stocks / NXT eligible {nxtEligibleCount} / mode {_settings.MarketMode}");
            return summary;
        }
    }
}
