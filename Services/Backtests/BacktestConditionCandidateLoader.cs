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
            IReadOnlyDictionary<string, StockMasterItem> stockMasterByCode =
                await BacktestMarketModeHelper.LoadStockMasterByCodeAsync(cancellationToken).ConfigureAwait(false);

            if (_settings.MaxConditionCandidates > 0)
                stocks = [.. stocks.Take(_settings.MaxConditionCandidates)];

            bool marketSplit = BacktestMarketModeHelper.IsMarketSplit(_settings);
            bool nxtOnly = BacktestMarketModeHelper.IsNxtOnly(_settings);
            bool sorMixed = BacktestMarketModeHelper.IsSorMixed(_settings);

            var candidates = new List<BacktestCandidate>();
            foreach ((string rawCode, string rawName) in stocks.Where(item => !string.IsNullOrWhiteSpace(item.Code)))
            {
                string code = BacktestDataStore.NormalizeCode(rawCode);
                bool supportsNxt = stockMasterByCode.TryGetValue(code, out StockMasterItem? master) && master.SupportsNxt;
                string name = string.IsNullOrWhiteSpace(rawName) && master != null ? master.Name : rawName;

                if (!nxtOnly)
                    candidates.Add(CreateCandidate(code, name, "KRX", supportsNxt, today, conditionSeq, conditionName, sourceName, importedAt, sorMixed));

                if ((marketSplit || nxtOnly) && supportsNxt)
                    candidates.Add(CreateCandidate(code, name, "NXT", supportsNxt, today, conditionSeq, conditionName, sourceName, importedAt, sorMixed));
            }

            return [.. candidates
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{item.Code}|{item.Market}|{item.CandidateDate}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Market)];

            static BacktestCandidate CreateCandidate(
                string code,
                string name,
                string market,
                bool supportsNxt,
                string candidateDate,
                string conditionSeq,
                string conditionName,
                string sourceName,
                DateTime importedAt,
                bool sorMixed)
            {
                string normalizedMarket = BacktestDataStore.NormalizeMarket(market);
                string memo = normalizedMarket == "NXT"
                    ? $"condition {conditionSeq}: {conditionName} / NXT market data candidate"
                    : $"condition {conditionSeq}: {conditionName}";
                if (sorMixed && supportsNxt)
                    memo += " / SOR mixed NXT eligible";

                return new BacktestCandidate
                {
                    Code = code,
                    Name = name,
                    Market = normalizedMarket,
                    CandidateDate = candidateDate,
                    NxtEnabled = supportsNxt,
                    StrategyCode = "BASE_CANDLE",
                    Source = "KIWOOM_CONDITION",
                    SourceName = sourceName,
                    Memo = memo,
                    ImportedAt = importedAt
                };
            }
        }
    }
}
