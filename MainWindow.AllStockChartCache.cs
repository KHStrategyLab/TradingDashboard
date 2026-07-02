using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TradingDashboard.Models;
using TradingDashboard.Services;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private const int AllStockChartCacheDailyCount = 600;
        private const int AllStockChartCacheFiveMinuteCount = 300;
        private const int AllStockChartCacheRequestsPerSecond = 4;
        private const int AllStockChartCacheMinute = 5;
        private static readonly TimeSpan AllStockChartCacheRateWindow = TimeSpan.FromSeconds(1);

        private readonly object _allStockChartCacheRateLock = new();
        private readonly Queue<DateTime> _allStockChartCacheRequestTimesUtc = new();
        private CancellationTokenSource? _allStockChartCacheCts;
        private Task? _allStockChartCacheTask;
        private bool _isUpdatingAllStockChartCacheToggle;
        private int _allStockChartCacheRunId;

        private void AllStockChartCacheToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingStrategyControls || _isUpdatingAllStockChartCacheToggle)
                return;

            if (AllStockChartCacheToggle?.IsChecked == true)
                StartAllStockChartCacheCollector();
            else
                StopAllStockChartCacheCollector();

            UpdateStrategyControlBoard();
        }

        private void StartAllStockChartCacheCollector()
        {
            if (_allStockChartCacheTask != null && !_allStockChartCacheTask.IsCompleted)
            {
                UpdateAllStockChartCacheStatus("RUNNING");
                return;
            }

            _allStockChartCacheCts?.Dispose();
            var cts = new CancellationTokenSource();
            _allStockChartCacheCts = cts;
            int runId = ++_allStockChartCacheRunId;
            _allStockChartCacheTask = Task.Run(() => RunAllStockChartCacheCollectorAsync(runId, cts));
            UpdateAllStockChartCacheStatus($"STARTING · Day {AllStockChartCacheDailyCount:N0} -> 5m {AllStockChartCacheFiveMinuteCount:N0} · <= {AllStockChartCacheRequestsPerSecond}/s");
            AppendLog($"all-stock chart cache start requested: Day {AllStockChartCacheDailyCount:N0} -> 5m {AllStockChartCacheFiveMinuteCount:N0} / <= {AllStockChartCacheRequestsPerSecond}/s");
        }

        private void StopAllStockChartCacheCollector()
        {
            if (_allStockChartCacheCts == null || _allStockChartCacheCts.IsCancellationRequested)
            {
                UpdateAllStockChartCacheStatus("OFF");
                return;
            }

            _allStockChartCacheCts.Cancel();
            UpdateAllStockChartCacheStatus("STOPPING");
            AppendLog("all-stock chart cache stop requested");
        }

        private bool IsAllStockChartCacheRunning()
        {
            return _allStockChartCacheTask != null &&
                !_allStockChartCacheTask.IsCompleted &&
                _allStockChartCacheCts?.IsCancellationRequested != true;
        }

        private async Task RunAllStockChartCacheCollectorAsync(int runId, CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            var sw = Stopwatch.StartNew();

            try
            {
                IReadOnlyList<AllStockChartCacheTarget> targets = await LoadAllStockChartCacheTargetsAsync(cancellationToken).ConfigureAwait(false);
                if (targets.Count == 0)
                {
                    UpdateAllStockChartCacheStatus("NO STOCK MASTER");
                    AppendLog("all-stock chart cache skipped: stock master empty");
                    return;
                }

                string expectedDate = ResolveAllStockChartCacheExpectedDate(DateTime.Now);
                AppendLog($"all-stock chart cache started: {targets.Count:N0}stocks / expected {expectedDate} / KRX=KRX, NXT=AL");

                AllStockChartCachePhaseResult daily = await RunAllStockChartCachePhaseAsync(
                    "Day",
                    targets,
                    ChartPeriod.Daily,
                    minute: 0,
                    AllStockChartCacheDailyCount,
                    expectedDate,
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                AllStockChartCachePhaseResult fiveMinute = await RunAllStockChartCachePhaseAsync(
                    "5m",
                    targets,
                    ChartPeriod.Minute5,
                    AllStockChartCacheMinute,
                    AllStockChartCacheFiveMinuteCount,
                    expectedDate,
                    cancellationToken).ConfigureAwait(false);

                string message =
                    $"DONE · Day {daily.SuccessCount:N0}/{targets.Count:N0}, 5m {fiveMinute.SuccessCount:N0}/{targets.Count:N0}, " +
                    $"skip {daily.SkippedFreshCount + fiveMinute.SkippedFreshCount:N0}, fail {daily.FailCount + fiveMinute.FailCount:N0}, {sw.Elapsed:hh\\:mm\\:ss}";
                UpdateAllStockChartCacheStatus(message);
                AppendReadyLog($"all-stock chart cache READY: {message}");
            }
            catch (OperationCanceledException)
            {
                UpdateAllStockChartCacheStatus($"STOPPED · {sw.Elapsed:hh\\:mm\\:ss}");
                AppendLog($"all-stock chart cache stopped: {sw.Elapsed:hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                UpdateAllStockChartCacheStatus($"ERROR · {ex.GetType().Name}");
                AppendLog($"all-stock chart cache error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (runId == _allStockChartCacheRunId)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _isUpdatingAllStockChartCacheToggle = true;
                        try
                        {
                            if (AllStockChartCacheToggle != null)
                                AllStockChartCacheToggle.IsChecked = false;
                        }
                        finally
                        {
                            _isUpdatingAllStockChartCacheToggle = false;
                        }

                        UpdateStrategyControlBoard();
                    });

                    if (ReferenceEquals(_allStockChartCacheCts, cts))
                    {
                        _allStockChartCacheCts?.Dispose();
                        _allStockChartCacheCts = null;
                        _allStockChartCacheTask = null;
                    }
                }
            }
        }

        private async Task<IReadOnlyList<AllStockChartCacheTarget>> LoadAllStockChartCacheTargetsAsync(CancellationToken cancellationToken)
        {
            StockMasterCacheDocument? document = await new StockMasterCacheStore()
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<StockMasterItem> items = document?.Items is { Count: > 0 }
                ? document.Items
                : await _kiwoomConditionService.GetStockMasterItemsAsync(cancellationToken).ConfigureAwait(false);

            return [.. (items ?? [])
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .Select(item =>
                {
                    string code = NormalizeStockCode(item.Code);
                    string market = item.SupportsNxt ? "AL" : "KRX";
                    string name = string.IsNullOrWhiteSpace(item.Name) ? code : item.Name.Trim();
                    return new AllStockChartCacheTarget(code, name, market, item.SupportsNxt);
                })
                .Where(target => !string.IsNullOrWhiteSpace(target.Code))
                .GroupBy(target => target.Code, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(target => target.SupportsNxt).First())
                .OrderBy(target => target.Code, StringComparer.Ordinal)];
        }

        private async Task<AllStockChartCachePhaseResult> RunAllStockChartCachePhaseAsync(
            string phaseName,
            IReadOnlyList<AllStockChartCacheTarget> targets,
            ChartPeriod period,
            int minute,
            int count,
            string expectedDate,
            CancellationToken cancellationToken)
        {
            int processed = 0;
            int success = 0;
            int skippedFresh = 0;
            int fail = 0;

            foreach (AllStockChartCacheTarget target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;

                if (HasFreshAllStockChartCache(target, period, count, expectedDate))
                {
                    skippedFresh++;
                    UpdateAllStockChartCacheStatus($"{phaseName} {processed:N0}/{targets.Count:N0} · fresh skip {skippedFresh:N0}");
                    continue;
                }

                try
                {
                    await WaitAllStockChartCacheRateLimitAsync(cancellationToken).ConfigureAwait(false);
                    IReadOnlyList<DailyCandle> candles = period == ChartPeriod.Daily
                        ? await _kiwoomConditionService.GetDailyCandlesByMarketAsync(target.Code, target.CacheMarket, count, cancellationToken).ConfigureAwait(false)
                        : await _kiwoomConditionService.GetMinuteCandlesByMarketAsync(target.Code, minute, target.CacheMarket, count, cancellationToken).ConfigureAwait(false);

                    if (candles.Count > 0)
                    {
                        _chartCandleSqliteCacheStore.Upsert(
                            target.Code,
                            target.CacheMarket,
                            period.ToString(),
                            candles,
                            count,
                            "ALL_STOCK_CACHE");
                        success++;
                    }
                    else
                    {
                        fail++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    fail++;
                    AppendLog($"all-stock chart cache {phaseName} error: {target.Code} / {target.CacheMarket} / {ex.GetType().Name}: {ex.Message}");
                }

                UpdateAllStockChartCacheStatus($"{phaseName} {processed:N0}/{targets.Count:N0} · saved {success:N0}, fresh {skippedFresh:N0}, fail {fail:N0}");
                if (processed % 50 == 0 || processed == targets.Count)
                {
                    AppendLog($"all-stock chart cache {phaseName}: {processed:N0}/{targets.Count:N0} / saved {success:N0} / fresh {skippedFresh:N0} / fail {fail:N0}");
                }
            }

            return new AllStockChartCachePhaseResult(success, skippedFresh, fail);
        }

        private bool HasFreshAllStockChartCache(
            AllStockChartCacheTarget target,
            ChartPeriod period,
            int count,
            string expectedDate)
        {
            try
            {
                if (!_chartCandleSqliteCacheStore.TryGet(target.Code, target.CacheMarket, period.ToString(), count, out List<DailyCandle> candles) ||
                    candles.Count < count)
                {
                    return false;
                }

                string latestDate = NormalizeAllStockChartCacheDate(candles[^1].Date);
                return latestDate.Length >= 8 &&
                    string.Equals(latestDate[..8], expectedDate, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                AppendLog($"all-stock chart cache freshness check skipped: {target.Code} / {target.CacheMarket} / {FormatChartPeriodLabel(period)} / {ex.Message}");
                return false;
            }
        }

        private async Task WaitAllStockChartCacheRateLimitAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                TimeSpan delay;
                lock (_allStockChartCacheRateLock)
                {
                    DateTime now = DateTime.UtcNow;
                    while (_allStockChartCacheRequestTimesUtc.Count > 0 &&
                        now - _allStockChartCacheRequestTimesUtc.Peek() >= AllStockChartCacheRateWindow)
                    {
                        _allStockChartCacheRequestTimesUtc.Dequeue();
                    }

                    if (_allStockChartCacheRequestTimesUtc.Count < AllStockChartCacheRequestsPerSecond)
                    {
                        _allStockChartCacheRequestTimesUtc.Enqueue(now);
                        return;
                    }

                    delay = AllStockChartCacheRateWindow - (now - _allStockChartCacheRequestTimesUtc.Peek());
                }

                if (delay < TimeSpan.FromMilliseconds(10))
                    delay = TimeSpan.FromMilliseconds(10);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        private void UpdateAllStockChartCacheStatus(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => UpdateAllStockChartCacheStatus(text));
                return;
            }

            if (AllStockChartCacheStatusText != null)
                AllStockChartCacheStatusText.Text = string.IsNullOrWhiteSpace(text) ? "OFF" : text;
        }

        private static string ResolveAllStockChartCacheExpectedDate(DateTime now)
        {
            DateTime date = now.Date;
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                date = date.AddDays(-1);

            return date.ToString("yyyyMMdd");
        }

        private static string NormalizeAllStockChartCacheDate(string text)
        {
            return new string([.. (text ?? string.Empty).Where(char.IsDigit)]);
        }

        private sealed record AllStockChartCacheTarget(string Code, string Name, string CacheMarket, bool SupportsNxt);

        private sealed record AllStockChartCachePhaseResult(int SuccessCount, int SkippedFreshCount, int FailCount);
    }
}
