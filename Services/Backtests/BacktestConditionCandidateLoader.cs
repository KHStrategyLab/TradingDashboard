using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestConditionCandidateLoader
    {
        private readonly KiwoomRestConditionService _kiwoomService;
        private readonly BacktestSettings _settings;

        public BacktestConditionCandidateLoader(
            KiwoomRestConditionService kiwoomService,
            BacktestSettings? settings = null)
        {
            _kiwoomService = kiwoomService;
            _settings = settings ?? new BacktestSettings();
        }

        public async Task<IReadOnlyList<BacktestCandidate>> LoadAsync(CancellationToken cancellationToken = default)
        {
            string conditionSeq = Math.Max(1, _settings.CandidateConditionIndex).ToString();
            string conditionName = string.IsNullOrWhiteSpace(_settings.CandidateConditionName)
                ? conditionSeq
                : _settings.CandidateConditionName.Trim();
            string sourceName = string.IsNullOrWhiteSpace(_settings.CandidateSourceName)
                ? conditionName
                : _settings.CandidateSourceName.Trim();
            string today = DateTime.Today.ToString("yyyyMMdd");
            DateTime importedAt = DateTime.Now;

            List<(string Code, string Name)> stocks = await _kiwoomService
                .GetConditionBaseStocksAsync(conditionSeq, cancellationToken)
                .ConfigureAwait(false);

            return [.. stocks
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .Select(item => new BacktestCandidate
                {
                    Code = BacktestDataStore.NormalizeCode(item.Code),
                    Name = item.Name,
                    Market = "KRX",
                    CandidateDate = today,
                    NxtEnabled = false,
                    StrategyCode = "BASE_CANDLE",
                    Source = "KIWOOM_CONDITION",
                    SourceName = sourceName,
                    Memo = $"condition {conditionSeq}: {conditionName}",
                    ImportedAt = importedAt
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{item.Code}|{item.Market}|{item.CandidateDate}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Code)];
        }
    }
}
