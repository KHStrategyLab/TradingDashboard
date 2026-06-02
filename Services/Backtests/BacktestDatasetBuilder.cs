using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestDatasetBuilder
    {
        private readonly KiwoomRestConditionService _kiwoomService;
        private readonly BacktestDataStore _dataStore;
        private readonly DailyBaseCandleVerifier _baseCandleVerifier;

        public BacktestDatasetBuilder(
            KiwoomRestConditionService kiwoomService,
            BacktestDataStore? dataStore = null,
            DailyBaseCandleVerifier? baseCandleVerifier = null)
        {
            _kiwoomService = kiwoomService;
            _dataStore = dataStore ?? new BacktestDataStore();
            _baseCandleVerifier = baseCandleVerifier ?? new DailyBaseCandleVerifier();
        }

        public async Task<BacktestDatasetUpdateSummary> BuildDailyDataStoreAsync(
            IEnumerable<BacktestCandidate> candidates,
            int lookbackBars = 120,
            long minTradingValue = 50_000_000_000,
            decimal minChangeRate = 20m,
            bool mirrorKrxBaseCandlesForNxt = false,
            CancellationToken cancellationToken = default)
        {
            List<BacktestCandidate> candidateList = [.. (candidates ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{BacktestDataStore.NormalizeCode(item.Code)}|{BacktestDataStore.NormalizeMarket(item.Market)}|{BacktestDataStore.NormalizeDate(item.CandidateDate)}", StringComparer.Ordinal)
                .Select(group => group.First())];

            var summary = new BacktestDatasetUpdateSummary
            {
                RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                CandidateCount = candidateList.Count
            };
            IReadOnlyDictionary<string, StockMasterItem> stockMasterByCode = mirrorKrxBaseCandlesForNxt
                ? await BacktestMarketModeHelper.LoadStockMasterByCodeAsync(cancellationToken).ConfigureAwait(false)
                : new Dictionary<string, StockMasterItem>(StringComparer.Ordinal);

            foreach (BacktestCandidate candidate in candidateList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                candidate.Code = BacktestDataStore.NormalizeCode(candidate.Code);
                candidate.Market = BacktestDataStore.NormalizeMarket(candidate.Market);
                candidate.CandidateDate = BacktestDataStore.NormalizeDate(candidate.CandidateDate);

                IReadOnlyList<BacktestDailyBar> existing = _dataStore.LoadDailyBars(candidate.Code, candidate.Market);
                bool shouldDownload = ShouldDownloadDailyBars(existing, lookbackBars);
                IReadOnlyList<BacktestDailyBar> barsForVerification = existing;

                if (shouldDownload)
                {
                    bool useNxtMarket = string.Equals(candidate.Market, "NXT", StringComparison.OrdinalIgnoreCase);
                    List<DailyCandle> candles = await _kiwoomService
                        .GetDailyCandlesAsync(candidate.Code, useNxtMarket, lookbackBars, cancellationToken)
                        .ConfigureAwait(false);

                    List<BacktestDailyBar> downloaded = ConvertDailyBars(candidate, candles);
                    int upserted = _dataStore.UpsertDailyBars(candidate.Code, candidate.Market, downloaded);
                    summary.DailyDownloadCount++;
                    summary.DailyBarUpsertCount += upserted;
                    barsForVerification = _dataStore.LoadDailyBars(candidate.Code, candidate.Market);
                    summary.Logs.Add($"daily downloaded: {candidate.Code} / {candidate.Market} / {downloaded.Count}bars");
                }
                else
                {
                    summary.DailyReusedCount++;
                    summary.Logs.Add($"daily reused: {candidate.Code} / {candidate.Market} / {existing.Count}bars");
                }

                IReadOnlyList<BacktestBaseCandle> baseCandles = _baseCandleVerifier.Verify(
                    candidate,
                    barsForVerification,
                    lookbackBars,
                    minTradingValue,
                    minChangeRate);
                summary.BaseCandleCount += _dataStore.UpsertBaseCandles(baseCandles);
                if (baseCandles.Count > 0)
                    summary.Logs.Add($"base candles verified: {candidate.Code} / {candidate.Market} / {baseCandles.Count}events");

                if (mirrorKrxBaseCandlesForNxt &&
                    candidate.NxtEnabled &&
                    string.Equals(candidate.Market, "KRX", StringComparison.OrdinalIgnoreCase) &&
                    baseCandles.Count > 0)
                {
                    IReadOnlyList<BacktestBaseCandle> mirrors =
                        BacktestMarketModeHelper.BuildNxtExecutionBaseCandles(baseCandles, stockMasterByCode);
                    int mirrored = _dataStore.UpsertBaseCandles(mirrors);
                    summary.BaseCandleCount += mirrored;
                    if (mirrored > 0)
                        summary.Logs.Add($"base candles mirrored for SOR/NXT execution: {candidate.Code} / {mirrored}events");
                }
            }

            return summary;
        }

        private static bool ShouldDownloadDailyBars(IReadOnlyList<BacktestDailyBar> existing, int lookbackBars)
        {
            if (existing.Count < Math.Max(1, lookbackBars))
                return true;

            string today = DateTime.Today.ToString("yyyyMMdd");
            return !existing.Any(bar => string.Equals(bar.Date, today, StringComparison.Ordinal));
        }

        private static List<BacktestDailyBar> ConvertDailyBars(BacktestCandidate candidate, IEnumerable<DailyCandle> candles)
        {
            List<DailyCandle> ordered = [.. (candles ?? [])
                .Where(candle => candle != null && !string.IsNullOrWhiteSpace(candle.Date))
                .OrderBy(candle => candle.Date)];

            List<BacktestDailyBar> bars = [];
            for (int i = 0; i < ordered.Count; i++)
            {
                DailyCandle candle = ordered[i];
                long open = (long)Math.Round(candle.Open);
                long high = (long)Math.Round(candle.High);
                long low = (long)Math.Round(candle.Low);
                long close = (long)Math.Round(candle.Close);
                long previousClose = i > 0 ? (long)Math.Round(ordered[i - 1].Close) : 0;
                long tradingValue = ResolveTradingValueWon(candle, close);

                bars.Add(new BacktestDailyBar
                {
                    Code = candidate.Code,
                    Market = candidate.Market,
                    Date = BacktestDataStore.NormalizeDate(candle.Date),
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = candle.Volume,
                    TradingValue = tradingValue,
                    PreviousClose = previousClose,
                    ChangeRate = previousClose > 0 ? (close - previousClose) / (decimal)previousClose * 100m : 0,
                    CloseLocationPercent = high > low ? (close - low) / (decimal)(high - low) * 100m : 0,
                    Status = string.Equals(BacktestDataStore.NormalizeDate(candle.Date), DateTime.Today.ToString("yyyyMMdd"), StringComparison.Ordinal)
                        ? "Provisional"
                        : "Confirmed"
                });
            }

            return bars;
        }

        private static long ResolveTradingValueWon(DailyCandle candle, long close)
        {
            long estimated = close > 0 && candle.Volume > 0
                ? (long)Math.Min(long.MaxValue, close * (double)candle.Volume)
                : 0;
            long raw = candle.TradingValue;
            if (raw <= 0)
                return estimated;

            long rawAsMillionWon = raw > long.MaxValue / 1_000_000 ? long.MaxValue : raw * 1_000_000;
            if (estimated <= 0)
                return rawAsMillionWon;

            long rawDistance = Math.Abs(raw - estimated);
            long millionWonDistance = Math.Abs(rawAsMillionWon - estimated);
            return millionWonDistance <= rawDistance ? rawAsMillionWon : Math.Max(raw, estimated);
        }
    }
}
