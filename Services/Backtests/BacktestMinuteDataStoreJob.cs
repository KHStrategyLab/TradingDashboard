using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestMinuteDataStoreJob
    {
        private readonly KiwoomRestConditionService _kiwoomService;
        private readonly BacktestDataStore _dataStore;
        private readonly BacktestSettings _settings;

        public BacktestMinuteDataStoreJob(
            KiwoomRestConditionService kiwoomService,
            BacktestSettings? settings = null,
            BacktestDataStore? dataStore = null)
        {
            _kiwoomService = kiwoomService;
            _settings = settings ?? new BacktestSettings();
            _dataStore = dataStore ?? new BacktestDataStore();
        }

        public async Task<BacktestMinuteDataStoreSummary> RunAsync(CancellationToken cancellationToken = default)
        {
            int mirroredBaseCandles = await MirrorKrxBaseCandlesForSorMixedAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<BacktestBaseCandle> baseCandles = _dataStore.LoadBaseCandles();
            List<BaseCandleGroup> groups = [.. baseCandles
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.BaseCandleDate))
                .GroupBy(item => $"{BacktestDataStore.NormalizeCode(item.Code)}|{BacktestDataStore.NormalizeMarket(item.Market)}", StringComparer.Ordinal)
                .Select(group => new BaseCandleGroup(
                    BacktestDataStore.NormalizeCode(group.First().Code),
                    BacktestDataStore.NormalizeMarket(group.First().Market),
                    group.Min(item => BacktestDataStore.NormalizeDate(item.BaseCandleDate)) ?? DateTime.Today.ToString("yyyyMMdd"),
                    group.Count()))
                .Where(item => BacktestMarketModeHelper.PassesMarketFilter(item.Market, _settings.MinuteMarketFilter))
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Market)];

            if (_settings.MaxMinuteStockMarketGroups > 0)
                groups = [.. groups.Take(_settings.MaxMinuteStockMarketGroups)];

            int[] intervals = ResolveIntervals(_settings.MinuteIntervals);
            int fetchCount = Math.Clamp(_settings.MinuteFetchCount, 60, 5000);
            var summary = new BacktestMinuteDataStoreSummary
            {
                RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                BaseCandleCount = baseCandles.Count,
                StockMarketCount = groups.Count,
                MinuteSetCount = groups.Count * intervals.Length
            };
            if (mirroredBaseCandles > 0)
                summary.Logs.Add($"base candles mirrored for SOR/NXT execution before minute download: {mirroredBaseCandles}events");
            if (!string.IsNullOrWhiteSpace(_settings.MinuteMarketFilter) || _settings.MaxMinuteStockMarketGroups > 0)
                summary.Logs.Add($"minute datastore filter: market={(_settings.MinuteMarketFilter.Length == 0 ? "ALL" : _settings.MinuteMarketFilter)} / groups={groups.Count} / max={_settings.MaxMinuteStockMarketGroups}");

            foreach (BaseCandleGroup group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (int minute in intervals)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    IReadOnlyList<BacktestMinuteBar> existing = _dataStore.LoadMinuteBars(group.Code, group.Market, minute);
                    if (!ShouldDownload(existing, group.EarliestBaseDate))
                    {
                        summary.MinuteReusedCount++;
                        summary.Logs.Add($"minute reused: {group.Code} / {group.Market} / {minute}m / {existing.Count}bars");
                        continue;
                    }

                    bool useNxtMarket = string.Equals(group.Market, "NXT", StringComparison.OrdinalIgnoreCase);
                    List<DailyCandle> candles = await _kiwoomService
                        .GetMinuteCandlesAsync(group.Code, minute, useNxtMarket, fetchCount, cancellationToken)
                        .ConfigureAwait(false);

                    List<BacktestMinuteBar> filtered = ConvertMinuteBars(group, minute, candles);
                    int upserted = _dataStore.UpsertMinuteBars(group.Code, group.Market, minute, filtered);
                    summary.MinuteDownloadCount++;
                    summary.MinuteBarUpsertCount += upserted;
                    summary.Logs.Add($"minute downloaded: {group.Code} / {group.Market} / {minute}m / {filtered.Count}bars / base>={group.EarliestBaseDate}");
                }
            }

            return summary;
        }

        private async Task<int> MirrorKrxBaseCandlesForSorMixedAsync(CancellationToken cancellationToken)
        {
            if (!BacktestMarketModeHelper.IsSorMixed(_settings))
                return 0;

            IReadOnlyDictionary<string, StockMasterItem> stockMasterByCode =
                await BacktestMarketModeHelper.LoadStockMasterByCodeAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<BacktestBaseCandle> mirrors =
                BacktestMarketModeHelper.BuildNxtExecutionBaseCandles(_dataStore.LoadBaseCandles(), stockMasterByCode);
            return _dataStore.UpsertBaseCandles(mirrors);
        }

        private static bool ShouldDownload(IReadOnlyList<BacktestMinuteBar> existing, string earliestBaseDate)
        {
            string threshold = $"{BacktestDataStore.NormalizeDate(earliestBaseDate)}000000";
            if (!existing.Any(bar => string.CompareOrdinal(bar.DateTime, threshold) >= 0))
                return true;

            string today = DateTime.Today.ToString("yyyyMMdd");
            string latestDate = existing
                .Where(bar => !string.IsNullOrWhiteSpace(bar.DateTime))
                .Select(bar => bar.DateTime.Length >= 8 ? bar.DateTime[..8] : string.Empty)
                .OrderBy(date => date)
                .LastOrDefault() ?? string.Empty;

            return !string.Equals(latestDate, today, StringComparison.Ordinal);
        }

        private static List<BacktestMinuteBar> ConvertMinuteBars(BaseCandleGroup group, int minute, IEnumerable<DailyCandle> candles)
        {
            string threshold = $"{BacktestDataStore.NormalizeDate(group.EarliestBaseDate)}000000";
            string today = DateTime.Today.ToString("yyyyMMdd");

            return [.. (candles ?? [])
                .Where(candle => candle != null && !string.IsNullOrWhiteSpace(candle.Date) && candle.Close > 0)
                .Select(candle => ToMinuteBar(group, minute, candle, today))
                .Where(bar => string.CompareOrdinal(bar.DateTime, threshold) >= 0)
                .GroupBy(bar => bar.DateTime, StringComparer.Ordinal)
                .Select(grouped => grouped.Last())
                .OrderBy(bar => bar.DateTime)];
        }

        private static BacktestMinuteBar ToMinuteBar(BaseCandleGroup group, int minute, DailyCandle candle, string today)
        {
            long open = (long)Math.Round(candle.Open);
            long high = (long)Math.Round(candle.High);
            long low = (long)Math.Round(candle.Low);
            long close = (long)Math.Round(candle.Close);

            return new BacktestMinuteBar
            {
                Code = group.Code,
                Market = group.Market,
                Minute = minute,
                DateTime = NormalizeMinuteDateTime(candle.Date),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = candle.Volume,
                TradingValue = ResolveTradingValueWon(candle, close),
                Status = NormalizeMinuteDateTime(candle.Date).StartsWith(today, StringComparison.Ordinal)
                    ? "Provisional"
                    : "Downloaded"
            };
        }

        private static string NormalizeMinuteDateTime(string value)
        {
            string digits = new([.. (value ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 14)
                return digits[..14];
            if (digits.Length >= 12)
                return $"{digits[..12]}00";
            if (digits.Length >= 8)
                return $"{digits[..8]}000000";
            return $"{DateTime.Today:yyyyMMdd}000000";
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

        private static int[] ResolveIntervals(IEnumerable<int>? intervals)
        {
            int[] resolved = [.. (intervals ?? [])
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)];

            return resolved.Length > 0 ? resolved : [1, 3, 5, 10, 15, 30];
        }

        private sealed record BaseCandleGroup(string Code, string Market, string EarliestBaseDate, int BaseCandleCount);
    }
}
