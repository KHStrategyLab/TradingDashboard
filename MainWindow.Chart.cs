using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TradingDashboard.Models;
using TradingDashboard.Services;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void DayChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Daily;
            ResetChartLoadMoreCount();
            ResetMinuteChartComboSelection();
            StartSelectedChartRender();
        }

        private void MinuteChartComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MinuteChartComboBox?.SelectedValue is not string selected || string.IsNullOrWhiteSpace(selected))
                return;

            if (Enum.TryParse(selected, out ChartPeriod period) && IsMinuteChartPeriod(period))
            {
                _currentChartPeriod = period;
                ResetChartLoadMoreCount();
                StartSelectedChartRender();
            }
        }

        private void MinuteChartComboBox_DropDownOpened(object sender, EventArgs e)
        {
            MinuteChartPlaceholderItem?.Visibility = Visibility.Collapsed;
        }

        private void MinuteChartComboBox_DropDownClosed(object sender, EventArgs e)
        {
            MinuteChartPlaceholderItem?.Visibility = Visibility.Visible;
        }

        private void ResetMinuteChartComboSelection()
        {
            if (MinuteChartComboBox != null && !IsMinuteChartPeriod(_currentChartPeriod))
                MinuteChartComboBox.SelectedIndex = 0;
        }

        private void ResetStartupChartPeriodToDaily()
        {
            _currentChartPeriod = ChartPeriod.Daily;
            _currentChartDataPeriod = ChartPeriod.Daily;
            ResetChartLoadMoreCount();
            ResetMinuteChartComboSelection();
        }

        private void WeekChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Weekly;
            ResetChartLoadMoreCount();
            ResetMinuteChartComboSelection();
            StartSelectedChartRender();
        }

        private void MonthChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Monthly;
            ResetChartLoadMoreCount();
            ResetMinuteChartComboSelection();
            StartSelectedChartRender();
        }

        private void ChartLoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsMinuteChartPeriod(_currentChartPeriod))
                return;

            _chartAdditionalCandleCount += ChartLoadMoreCandleStep;
            StartSelectedChartRender();
        }

        private void ResetChartLoadMoreCount()
        {
            _chartAdditionalCandleCount = 0;
        }

        private void StartSelectedChartRender()
        {
            if (string.IsNullOrWhiteSpace(_selectedStockCode))
                return;

            int selectionVersion = _selectionVersion;
            int chartVersion = ++_chartRenderVersion;
            CancellationTokenSource? previousChartCts = _chartRequestCts;
            previousChartCts?.Cancel();
            DisposeCanceledRequestLater(previousChartCts);

            _chartRequestCts = _selectedRequestCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(_selectedRequestCts.Token)
                : new CancellationTokenSource();

            _ = RenderSelectedChartAsync(selectionVersion, chartVersion, _selectedStockCode, _chartRequestCts.Token);
        }

        private async Task RenderSelectedChartAsync(int selectionVersion, int chartVersion, string selectedStockCode, CancellationToken cancellationToken = default)
        {
            if (MainChartHost == null || VolumeChartHost == null)
                return;

            int count = ResolveCurrentChartCandleCount(_currentChartPeriod);

            if (string.IsNullOrWhiteSpace(selectedStockCode))
                return;

            try
            {
                ChartPeriod requestedPeriod = _currentChartPeriod;
                string marketLabel = ResolveDisplayMarketForStockCode(selectedStockCode);
                bool useNxtMarket = string.Equals(marketLabel, "NXT", StringComparison.Ordinal);
                ChartCacheKey cacheKey = CreateChartCacheKey(selectedStockCode, useNxtMarket, requestedPeriod);
                bool showedCachedChart = false;
                if (TryGetChartMemoryCache(cacheKey, count, out List<ChartCandle> cachedCandles))
                {
                    if (selectionVersion != _selectionVersion || chartVersion != _chartRenderVersion || selectedStockCode != _selectedStockCode || requestedPeriod != _currentChartPeriod)
                        return;

                    ApplyChartCandles(cachedCandles, selectedStockCode, marketLabel, requestedPeriod, $"{marketLabel} cache");
                    showedCachedChart = true;
                }
                else if (TryGetChartFileCache(cacheKey, count, out List<ChartCandle> fileCachedCandles))
                {
                    if (selectionVersion != _selectionVersion || chartVersion != _chartRenderVersion || selectedStockCode != _selectedStockCode || requestedPeriod != _currentChartPeriod)
                        return;

                    SetChartMemoryCache(cacheKey, fileCachedCandles);
                    ApplyChartCandles(fileCachedCandles, selectedStockCode, marketLabel, requestedPeriod, $"{marketLabel} file cache");
                    showedCachedChart = true;
                }

                List<ChartCandle> candles;
                if (IsMinuteChartPeriod(requestedPeriod))
                {
                    candles = [.. (await _kiwoomConditionService.GetMinuteCandlesAsync(selectedStockCode, ResolveMinuteChartInterval(requestedPeriod), useNxtMarket, count, cancellationToken))
                        .TakeLast(count)
                        .Select(ToChartCandle)];
                }
                else
                {
                    candles = requestedPeriod switch
                    {
                        ChartPeriod.Daily => [.. (await _kiwoomConditionService.GetDailyCandlesAsync(selectedStockCode, useNxtMarket, 300, cancellationToken))
                            .TakeLast(count)
                            .Select(ToChartCandle)],
                        ChartPeriod.Weekly => [.. (await _kiwoomConditionService.GetWeeklyCandlesAsync(selectedStockCode, useNxtMarket, count, cancellationToken)).Select(ToChartCandle)],
                        ChartPeriod.Monthly => [.. (await _kiwoomConditionService.GetMonthlyCandlesAsync(selectedStockCode, useNxtMarket, count, cancellationToken)).Select(ToChartCandle)],
                        _ => [.. (await _kiwoomConditionService.GetDailyCandlesAsync(selectedStockCode, useNxtMarket, 300, cancellationToken))
                            .TakeLast(count)
                            .Select(ToChartCandle)]
                    };
                }
                if (selectionVersion != _selectionVersion || chartVersion != _chartRenderVersion || selectedStockCode != _selectedStockCode || requestedPeriod != _currentChartPeriod)
                    return;

                if (candles.Count == 0)
                    return;

                SetChartMemoryCache(cacheKey, candles, count);
                ApplyChartCandles(candles, selectedStockCode, marketLabel, requestedPeriod, showedCachedChart ? $"{marketLabel} refresh" : $"{marketLabel} initial");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"chart query error: {ex.Message}");
            }
        }

        private void ApplyChartCandles(List<ChartCandle> candles, string selectedStockCode, string market, ChartPeriod period, string reason)
        {
            string normalizedMarket = NormalizeIdentityMarket(market);
            _currentChartCandles.Clear();
            _currentChartCandles.AddRange(CloneChartCandles(candles));
            _currentChartCode = selectedStockCode;
            _currentChartMarket = normalizedMarket;
            _currentChartDataPeriod = period;
            _lastRealtimeChartDrawAt = DateTime.MinValue;
            ResetChartViewport();
            ApplySelectedDisplayPriceToLoadedChart(selectedStockCode, reason);

            if (period == ChartPeriod.Daily &&
                candles.Count > 0 &&
                TryGetWatchStockForMarket(selectedStockCode, normalizedMarket, out WatchStockItem? stock) &&
                stock != null)
            {
                ChartCandle latest = candles[^1];
                ApplyMiniDailyCandle(
                    stock,
                    (long)Math.Round(latest.Open),
                    (long)Math.Round(latest.High),
                    (long)Math.Round(latest.Low),
                    (long)Math.Round(latest.Close),
                    string.Equals(normalizedMarket, "NXT", StringComparison.Ordinal));
            }

            DrawFullChart(reason);
            UpdateStrategyProgressRows();
        }

        private void ApplySelectedDisplayPriceToLoadedChart(string selectedStockCode, string reason)
        {
            if (_currentChartCandles.Count == 0 ||
                string.IsNullOrWhiteSpace(selectedStockCode) ||
                !TryGetWatchStockForMarket(selectedStockCode, _currentChartMarket, out WatchStockItem? stock) ||
                stock == null ||
                stock.CurrentPrice <= 0)
            {
                return;
            }

            bool chartIsNxt = string.Equals(_currentChartMarket, "NXT", StringComparison.Ordinal);
            bool displayIsNxt = string.Equals(stock.DisplayPriceMarket, "NXT", StringComparison.OrdinalIgnoreCase);
            if (chartIsNxt != displayIsNxt)
                return;

            ApplyDisplayPriceToLastChartCandle(stock.CurrentPrice);
        }

        private ChartCacheKey CreateChartCacheKey(string stockCode, bool useNxtMarket, ChartPeriod period)
        {
            return new ChartCacheKey(NormalizeStockCode(stockCode), useNxtMarket, period);
        }

        private bool TryGetChartMemoryCache(ChartCacheKey key, int count, out List<ChartCandle> candles)
        {
            if (_chartMemoryCache.TryGetValue(key, out ChartCacheEntry? entry) && entry.Candles.Count > 0)
            {
                if (entry.Candles.Count < count)
                {
                    candles = [];
                    return false;
                }

                entry.LastAccess = ++_chartCacheAccessSequence;
                candles = CloneChartCandles(entry.Candles.TakeLast(count));
                return candles.Count > 0;
            }

            candles = [];
            return false;
        }

        private bool TryGetChartFileCache(ChartCacheKey key, int count, out List<ChartCandle> candles)
        {
            if (!IsCalendarChartPeriod(key.Period) ||
                !_chartCandleFileCacheStore.TryGet(key.Code, key.UseNxtMarket, key.Period.ToString(), count, out List<DailyCandle> fileCandles))
            {
                candles = [];
                return false;
            }

            candles = [.. fileCandles.TakeLast(count).Select(ToChartCandle)];
            return candles.Count > 0;
        }

        private void SetChartMemoryCache(ChartCacheKey key, IEnumerable<ChartCandle> candles)
        {
            SetChartMemoryCache(key, candles, ResolveChartCandleCount(key.Period));
        }

        private void SetChartMemoryCache(ChartCacheKey key, IEnumerable<ChartCandle> candles, int retainCount)
        {
            List<ChartCandle> snapshot = CloneChartCandles(candles.TakeLast(Math.Max(1, retainCount)));
            if (snapshot.Count == 0)
                return;

            _chartMemoryCache[key] = new ChartCacheEntry
            {
                Candles = snapshot,
                CachedAt = DateTime.Now,
                LastAccess = ++_chartCacheAccessSequence
            };
            TrimChartMemoryCache();
        }

        private void TrimChartMemoryCache()
        {
            while (_chartMemoryCache.Count > MaxChartMemoryCacheEntries || _chartMemoryCache.Values.Sum(e => e.Candles.Count) > MaxChartMemoryCacheCandles)
            {
                ChartCacheKey oldestKey = _chartMemoryCache
                    .OrderBy(kv => kv.Value.LastAccess)
                    .Select(kv => kv.Key)
                    .FirstOrDefault();

                if (!_chartMemoryCache.Remove(oldestKey))
                    break;
            }
        }

        private static List<ChartCandle> CloneChartCandles(IEnumerable<ChartCandle> candles)
        {
            return [.. candles
                .Where(c => c != null)
                .Select(CloneChartCandle)];
        }

        private void StartInitialChartFileCachePreload(IEnumerable<WatchStockItem> stocks)
        {
            if (_initialChartFileCachePreloadStarted || !_config.Kiwoom.UseRestApi)
                return;

            List<ChartPreloadStock> snapshot = [.. stocks
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => BuildWatchStockIdentityKey(stock), StringComparer.Ordinal)
                .Select(group =>
                {
                    WatchStockItem stock = group.First();
                    bool useNxtMarket = ShouldUseNxtDataForStock(stock);
                    return new ChartPreloadStock(stock.Code, useNxtMarket);
                })];

            if (snapshot.Count == 0)
                return;

            _initialChartFileCachePreloadStarted = true;
            _ = Task.Run(() => PreloadInitialChartFileCacheAsync(snapshot));
        }

        private async Task PreloadInitialChartFileCacheAsync(IReadOnlyList<ChartPreloadStock> stocks)
        {
            var sw = Stopwatch.StartNew();
            int savedSets = 0;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Dispatcher.Invoke(() => AppendLog($"chart daily file cache preload started: {stocks.Count}stocks"));

                foreach (ChartPreloadStock stock in stocks)
                {
                    List<DailyCandle> daily = await _kiwoomConditionService
                        .GetDailyCandlesAsync(stock.Code, stock.UseNxtMarket, ResolveChartCandleCount(ChartPeriod.Daily), CancellationToken.None)
                        .ConfigureAwait(false);
                    if (daily.Count > 0)
                    {
                        _chartCandleFileCacheStore.Upsert(
                            stock.Code,
                            stock.UseNxtMarket,
                            ChartPeriod.Daily.ToString(),
                            daily,
                            ResolveChartFileCacheRetainCount(ChartPeriod.Daily));
                        savedSets++;
                    }
                }

                if (savedSets > 0)
                {
                    _chartCandleFileCacheStore.Save();
                    Dispatcher.Invoke(() => AppendLog($"chart daily file cache saved: {stocks.Count}stocks / {savedSets}sets / {sw.ElapsedMilliseconds:N0}ms"));
                    Dispatcher.Invoke(() => AppendReadyLog("Daily chart cache READY"));
                }
                else
                {
                    Dispatcher.Invoke(() => AppendLog($"chart daily file cache save skipped: {stocks.Count}stocks / no data"));
                    Dispatcher.Invoke(() => AppendReadyLog("Daily chart cache READY"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"chart daily file cache preload skipped: {ex.Message}"));
            }
        }

        private void StartBalancePriorityChartDataPreload(IEnumerable<WatchStockItem> stocks)
        {
            List<ChartPreloadStock> snapshot = [.. (stocks ?? [])
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => BuildWatchStockIdentityKey(stock), StringComparer.Ordinal)
                .Select(group =>
                {
                    WatchStockItem stock = group.First();
                    bool useNxtMarket = ShouldUseNxtDataForStock(stock);
                    return new ChartPreloadStock(stock.Code, useNxtMarket);
                })];

            if (snapshot.Count == 0)
                return;

            _ = PreloadBalancePriorityChartDataAsync(snapshot);
        }

        private async Task PreloadBalancePriorityChartDataAsync(IReadOnlyList<ChartPreloadStock> stocks)
        {
            var sw = Stopwatch.StartNew();
            int loadedSets = 0;
            Dispatcher.Invoke(() => AppendLog($"balance priority chart preload started: {stocks.Count}stocks / Day+1m+3m+5m"));

            foreach (ChartPreloadStock stock in stocks)
            {
                try
                {
                    loadedSets += await PreloadBalancePriorityChartPeriodAsync(stock, ChartPeriod.Daily, 0).ConfigureAwait(false);
                    loadedSets += await PreloadBalancePriorityChartPeriodAsync(stock, ChartPeriod.Minute1, 1).ConfigureAwait(false);
                    loadedSets += await PreloadBalancePriorityChartPeriodAsync(stock, ChartPeriod.Minute3, 3).ConfigureAwait(false);
                    loadedSets += await PreloadBalancePriorityChartPeriodAsync(stock, ChartPeriod.Minute5, 5).ConfigureAwait(false);
                    Dispatcher.Invoke(() => AppendLog($"balance priority chart preload stock done: {stock.Code} / Day+1m+3m+5m"));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"balance priority chart preload error: {stock.Code} / {ex.GetType().Name}: {ex.Message}"));
                }
            }

            Dispatcher.Invoke(() => AppendReadyLog($"balance priority chart preload READY: {stocks.Count}stocks / {loadedSets}sets / {sw.ElapsedMilliseconds:N0}ms"));
        }

        private async Task<int> PreloadBalancePriorityChartPeriodAsync(ChartPreloadStock stock, ChartPeriod period, int minute)
        {
            int count = ResolveChartCandleCount(period);
            ChartCacheKey key = CreateChartCacheKey(stock.Code, stock.UseNxtMarket, period);
            if (TryGetChartMemoryCache(key, count, out List<ChartCandle> cachedCandles))
            {
                return SeedStrategyMinuteFromChartCacheIfNeeded(stock, period, minute, cachedCandles, count) ? 1 : 0;
            }

            if (period == ChartPeriod.Daily &&
                TryGetChartFileCache(key, count, out List<ChartCandle> fileCachedCandles))
            {
                SetChartMemoryCache(key, fileCachedCandles, count);
                return 1;
            }

            if (period == ChartPeriod.Daily)
            {
                List<DailyCandle> daily = await _kiwoomConditionService
                    .GetDailyCandlesAsync(stock.Code, stock.UseNxtMarket, count, CancellationToken.None)
                    .ConfigureAwait(false);
                if (daily.Count <= 0)
                    return 0;

                SetChartMemoryCache(key, daily.Select(ToChartCandle), count);
                _chartCandleFileCacheStore.Upsert(
                    stock.Code,
                    stock.UseNxtMarket,
                    ChartPeriod.Daily.ToString(),
                    daily,
                    ResolveChartFileCacheRetainCount(ChartPeriod.Daily));
                _chartCandleFileCacheStore.Save();
                return 1;
            }

            List<DailyCandle> minuteCandles = await _kiwoomConditionService
                .GetMinuteCandlesAsync(stock.Code, minute, stock.UseNxtMarket, count)
                .ConfigureAwait(false);
            if (minuteCandles.Count <= 0)
                return 0;

            List<ChartCandle> chartCandles = [.. minuteCandles.TakeLast(count).Select(ToChartCandle)];
            SetChartMemoryCache(key, chartCandles, count);

            string market = stock.UseNxtMarket ? "NXT" : "KRX";
            _strategyMinuteCacheService.Seed(stock.Code, market, minute, minuteCandles, Math.Min(count, Math.Max(1, minuteCandles.Count)));
            return 1;
        }

        private bool SeedStrategyMinuteFromChartCacheIfNeeded(
            ChartPreloadStock stock,
            ChartPeriod period,
            int minute,
            IReadOnlyList<ChartCandle> cachedCandles,
            int targetCount)
        {
            if (!IsMinuteChartPeriod(period) || minute <= 0 || cachedCandles == null || cachedCandles.Count == 0)
                return false;

            string market = stock.UseNxtMarket ? "NXT" : "KRX";
            _strategyMinuteCacheService.Seed(
                stock.Code,
                market,
                minute,
                ConvertChartCandlesToDailyCandles(cachedCandles),
                Math.Min(targetCount, cachedCandles.Count));
            return true;
        }

        private static ChartCandle CloneChartCandle(ChartCandle c)
        {
            return new ChartCandle
            {
                Date = c.Date,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            };
        }

        private void DrawPriceChart(List<ChartCandle> candles)
        {
            if (candles.Count == 0)
            {
                ClearSelectedChartVisuals();
                return;
            }

            MainChartHost.Children.Clear();
            var canvas = new Canvas { Background = Brushes.Transparent };
            MainChartHost.Children.Add(canvas);
            _priceChartCanvas = canvas;
            AttachChartDragHandlers(canvas);
            _priceChartRenderState = null;
            _lastCandleWick = null;
            _lastCandleBody = null;
            _currentPriceMarkerLine = null;
            _currentPriceMarkerLabel = null;
            _currentPriceMarkerText = null;

            MainChartHost.UpdateLayout();
            double w = Math.Max(100, MainChartHost.ActualWidth - 2);
            double h = Math.Max(100, MainChartHost.ActualHeight - 2);
            canvas.Width = w;
            canvas.Height = h;
            const double axisWidth = 68;
            double chartW = Math.Max(40, w - axisWidth - ChartRightPadding);
            double currentPrice = ResolveSelectedCurrentPrice();

            double max = candles.Max(c => c.High);
            double min = candles.Min(c => c.Low);
            if (currentPrice > 0)
            {
                max = Math.Max(max, currentPrice);
                min = Math.Min(min, currentPrice);
            }
            double lastClose = candles.Last().Close;
            double tick = lastClose switch
            {
                >= 500000 => 1000,
                >= 100000 => 500,
                >= 50000 => 100,
                >= 10000 => 50,
                >= 5000 => 10,
                >= 1000 => 5,
                _ => 1
            };
            double axisPad = Math.Max(tick * 3, (max - min) * 0.08);
            max += axisPad;
            min = Math.Max(1, min - axisPad);
            double range = Math.Max(1, max - min);
            double candleW = Math.Max(2, chartW / candles.Count * 0.62);
            double gap = chartW / candles.Count;
            int visibleStartIndex = GetVisibleChartStartIndex();

            for (int i = 0; i < candles.Count; i++)
            {
                ChartCandle c = candles[i];
                double x = i * gap + (gap - candleW) / 2.0;
                double yHigh = (max - c.High) / range * (h - 4) + 2;
                double yLow = (max - c.Low) / range * (h - 4) + 2;
                double yOpen = (max - c.Open) / range * (h - 4) + 2;
                double yClose = (max - c.Close) / range * (h - 4) + 2;
                Brush upDown = c.Close >= c.Open ? _upColorBrush : _downColorBrush;

                var wick = new Line
                {
                    X1 = x + candleW / 2, X2 = x + candleW / 2,
                    Y1 = yHigh, Y2 = yLow,
                    Stroke = upDown, StrokeThickness = 1
                };
                canvas.Children.Add(wick);

                var body = new Rectangle
                {
                    Width = candleW,
                    Height = Math.Max(1, Math.Abs(yClose - yOpen)),
                    Fill = upDown
                };
                Canvas.SetLeft(body, x);
                Canvas.SetTop(body, Math.Min(yOpen, yClose));
                canvas.Children.Add(body);

                if (i == candles.Count - 1)
                {
                    _lastCandleWick = wick;
                    _lastCandleBody = body;
                }
            }

            DrawPredayRangeBreakoutSignals(canvas, candles.Count, visibleStartIndex, chartW, h, min, max);
            DrawMovingAverage(canvas, candles.Count, visibleStartIndex, 5, (Brush)FindResource("Ma5Brush"), chartW, h, min, max);
            DrawMovingAverage(canvas, candles.Count, visibleStartIndex, 10, (Brush)FindResource("Ma10Brush"), chartW, h, min, max);
            DrawMovingAverage(canvas, candles.Count, visibleStartIndex, 20, (Brush)FindResource("Ma20Brush"), chartW, h, min, max);
            DrawMovingAverage(canvas, candles.Count, visibleStartIndex, 60, (Brush)FindResource("Ma60Brush"), chartW, h, min, max);
            if (IsMinuteChartPeriod(_currentChartDataPeriod))
            {
                DrawMovingAverage(canvas, candles.Count, visibleStartIndex, 240, (Brush)FindResource("Ma240Brush"), chartW, h, min, max);
                DrawMovingAverage(canvas, candles.Count, visibleStartIndex, 480, (Brush)FindResource("Ma480Brush"), chartW, h, min, max);
            }

            DrawRightPriceAxis(canvas, chartW + ChartRightPadding, axisWidth, h, min, max, tick);
            DrawCurrentPriceMarker(canvas, chartW, ChartRightPadding, axisWidth, h, min, max, currentPrice);
            _priceChartRenderState = new ChartRenderState(candles.Count, GetVisibleChartStartIndex(), chartW, h, min, max, gap, candleW, 0, 0);
        }

        private double ResolveSelectedCurrentPrice()
        {
            string chartMarket = NormalizeIdentityMarket(_currentChartMarket);
            if (!string.IsNullOrWhiteSpace(_selectedStockCode) &&
                TryGetWatchStockForMarket(_selectedStockCode, chartMarket, out WatchStockItem? selected) &&
                selected != null &&
                string.Equals(NormalizeIdentityMarket(selected.DisplayPriceMarket), chartMarket, StringComparison.Ordinal) &&
                selected.CurrentPrice > 0)
            {
                return selected.CurrentPrice;
            }

            return 0;
        }

        private void ApplyRealtimeCalendarChartTick(string code, string market, long price, long cumulativeVolume, long tradeVolume)
        {
            ChartPeriod period = _currentChartDataPeriod;
            if (!IsCalendarChartPeriod(period) ||
                string.IsNullOrWhiteSpace(code) ||
                !string.Equals(code, _currentChartCode, StringComparison.Ordinal) ||
                !string.Equals(NormalizeIdentityMarket(market), _currentChartMarket, StringComparison.Ordinal) ||
                price <= 0 ||
                _currentChartCandles.Count == 0)
            {
                return;
            }

            DateTime now = DateTime.Now;
            ChartCandle last = _currentChartCandles[^1];
            bool isNewCandle = false;
            bool viewportSnapped = false;
            bool shouldSnapViewportToLatest = IsChartViewportNearLatest();
            if (!IsSameCalendarChartBucket(last.Date, period, now))
            {
                isNewCandle = true;
                last = new ChartCandle
                {
                    Date = BuildCalendarChartBucketDate(period, now),
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    Volume = Math.Max(0, period == ChartPeriod.Daily && cumulativeVolume > 0 ? cumulativeVolume : tradeVolume)
                };

                _currentChartCandles.Add(last);
                if (_currentChartCandles.Count > ResolveCurrentChartCandleCount(period))
                {
                    _currentChartCandles.RemoveAt(0);
                    _chartViewStartIndex = Math.Max(0, _chartViewStartIndex - 1);
                }

                if (shouldSnapViewportToLatest)
                    viewportSnapped = SnapChartViewportToLatestIfNear(force: true);
            }
            else
            {
                viewportSnapped = SnapChartViewportToLatestIfNear();
                if (last.Open <= 0)
                    last.Open = price;
                last.Close = price;
                last.High = Math.Max(last.High > 0 ? last.High : price, price);
                last.Low = Math.Min(last.Low > 0 ? last.Low : price, price);

                if (cumulativeVolume > 0)
                {
                    if (period == ChartPeriod.Daily)
                        last.Volume = cumulativeVolume;
                    else if (tradeVolume > 0)
                        last.Volume += tradeVolume;
                }
                else if (tradeVolume > 0)
                {
                    last.Volume += tradeVolume;
                }
            }

            if (viewportSnapped)
            {
                _lastRealtimeChartDrawAt = DateTime.Now;
                DrawFullChart(_currentChartCandles, $"{FormatChartPeriodLabel(period)} snap to latest");
                return;
            }

            if (!isNewCandle && (DateTime.Now - _lastRealtimeChartDrawAt).TotalMilliseconds < DailyChartRealtimeDrawIntervalMs)
            {
                if (TryUpdateLastChartVisual(last))
                    return;
            }

            if (!isNewCandle && TryUpdateLastChartVisual(last))
                return;

            _lastRealtimeChartDrawAt = DateTime.Now;
            DrawFullChart(_currentChartCandles, $"{FormatChartPeriodLabel(period)} realtime");
        }

        private void ApplyRealtimeChartTick(string code, string market, long price, long cumulativeVolume, long tradeVolume, string tradeTimeText)
        {
            if (IsMinuteChartPeriod(_currentChartPeriod))
            {
                ApplyRealtimeMinuteChartTick(code, market, price, tradeVolume, tradeTimeText, ResolveMinuteChartInterval(_currentChartPeriod));
                return;
            }

            ApplyRealtimeCalendarChartTick(code, market, price, cumulativeVolume, tradeVolume);
        }

        private void ApplySelectedChartDisplayPrice(string code, long price, string market, string source)
        {
            if (price <= 0 ||
                string.IsNullOrWhiteSpace(code) ||
                !string.Equals(NormalizeStockCode(code), NormalizeStockCode(_currentChartCode), StringComparison.Ordinal) ||
                _currentChartCandles.Count == 0)
            {
                return;
            }

            string sourceMarket = NormalizeIdentityMarket(market);
            if (!string.Equals(sourceMarket, _currentChartMarket, StringComparison.Ordinal))
                return;

            ApplyDisplayPriceToLastChartCandle(price);
            ChartCandle last = _currentChartCandles[^1];

            if (TryUpdateLastChartVisual(last))
                return;

            DrawFullChart(_currentChartCandles, $"{FormatChartPeriodLabel(_currentChartDataPeriod)} display price {source}");
        }

        private void ApplyDisplayPriceToLastChartCandle(long price)
        {
            if (price <= 0 || _currentChartCandles.Count == 0)
                return;

            ChartCandle last = _currentChartCandles[^1];
            if (last.Open <= 0)
                last.Open = price;
            last.Close = price;
            last.High = Math.Max(last.High > 0 ? last.High : price, price);
            last.Low = Math.Min(last.Low > 0 ? last.Low : price, price);
        }

        private void ApplyRealtimeMinuteChartTick(string code, string market, long price, long tradeVolume, string tradeTimeText, int minute)
        {
            if (!IsMinuteChartPeriod(_currentChartDataPeriod) ||
                string.IsNullOrWhiteSpace(code) ||
                !string.Equals(code, _currentChartCode, StringComparison.Ordinal) ||
                !string.Equals(NormalizeIdentityMarket(market), _currentChartMarket, StringComparison.Ordinal) ||
                price <= 0 ||
                _currentChartCandles.Count == 0)
            {
                return;
            }

            string bucketTime = BuildMinuteBucketTime(tradeTimeText, minute);
            ChartCandle last = _currentChartCandles[^1];
            bool isNewCandle = false;
            bool viewportSnapped = false;
            bool shouldSnapViewportToLatest = IsChartViewportNearLatest();
            if (!IsSameChartDate(last.Date, bucketTime))
            {
                isNewCandle = true;
                last = new ChartCandle
                {
                    Date = bucketTime,
                    Open = price,
                    High = price,
                    Low = price,
                    Close = price,
                    Volume = Math.Max(0, tradeVolume)
                };

                _currentChartCandles.Add(last);
                if (_currentChartCandles.Count > ResolveCurrentChartCandleCount(_currentChartDataPeriod))
                {
                    _currentChartCandles.RemoveAt(0);
                    _chartViewStartIndex = Math.Max(0, _chartViewStartIndex - 1);
                }

                if (shouldSnapViewportToLatest)
                    viewportSnapped = SnapChartViewportToLatestIfNear(force: true);
            }
            else
            {
                viewportSnapped = SnapChartViewportToLatestIfNear();
                if (last.Open <= 0)
                    last.Open = price;
                last.Close = price;
                last.High = Math.Max(last.High > 0 ? last.High : price, price);
                last.Low = Math.Min(last.Low > 0 ? last.Low : price, price);
                if (tradeVolume > 0)
                    last.Volume += tradeVolume;
            }

            if (viewportSnapped)
            {
                _lastRealtimeChartDrawAt = DateTime.Now;
                DrawFullChart(_currentChartCandles, $"{FormatChartPeriodLabel(_currentChartDataPeriod)} snap to latest");
                return;
            }

            if (!isNewCandle && (DateTime.Now - _lastRealtimeChartDrawAt).TotalMilliseconds < MinuteChartRealtimeDrawIntervalMs)
            {
                if (TryUpdateLastChartVisual(last))
                    return;
            }

            if (!isNewCandle && TryUpdateLastChartVisual(last))
                return;

            _lastRealtimeChartDrawAt = DateTime.Now;
            DrawFullChart(_currentChartCandles, $"{FormatChartPeriodLabel(_currentChartDataPeriod)} axis recalculation");
        }

        private void DrawFullChart(List<ChartCandle> candles, string reason)
        {
            DrawFullChart(reason);
        }

        private void DrawFullChart(string reason)
        {
            List<ChartCandle> candles = GetVisibleChartCandles();
            var sw = Stopwatch.StartNew();
            DrawPriceChart(candles);
            DrawVolumeChart(candles);
            sw.Stop();

            AppendLog($"chart full render({FormatChartPeriodLabel(_currentChartDataPeriod)} / {reason}): {candles.Count}bars / {sw.ElapsedMilliseconds:N0}ms");
        }

        private void ResetChartViewport()
        {
            _chartViewStartIndex = 0;
            _chartViewCount = 0;
        }

        private bool IsChartViewportNearLatest()
        {
            if (_chartViewCount <= 0 || _currentChartCandles.Count == 0)
                return false;

            int visibleEndIndex = Math.Min(_currentChartCandles.Count - 1, _chartViewStartIndex + _chartViewCount - 1);
            int distanceFromLatest = Math.Max(0, _currentChartCandles.Count - 1 - visibleEndIndex);
            int snapDistance = ResolveChartRealtimeSnapToLatestDistance(_currentChartDataPeriod);
            return distanceFromLatest <= snapDistance;
        }

        private bool SnapChartViewportToLatestIfNear(bool force = false)
        {
            if (_chartViewCount <= 0 || _currentChartCandles.Count == 0)
                return false;

            if (!force && !IsChartViewportNearLatest())
                return false;

            int nextStartIndex = Math.Max(0, _currentChartCandles.Count - _chartViewCount);
            if (nextStartIndex == _chartViewStartIndex)
                return false;

            _chartViewStartIndex = nextStartIndex;
            return true;
        }

        private List<ChartCandle> GetVisibleChartCandles()
        {
            if (_currentChartCandles.Count == 0)
                return [];

            int start = _chartViewCount > 0
                ? Math.Clamp(_chartViewStartIndex, 0, Math.Max(0, _currentChartCandles.Count - 1))
                : 0;
            int count = _chartViewCount > 0
                ? Math.Clamp(_chartViewCount, 1, _currentChartCandles.Count - start)
                : _currentChartCandles.Count;

            return [.. _currentChartCandles.Skip(start).Take(count)];
        }

        private int GetVisibleChartStartIndex()
        {
            if (_currentChartCandles.Count == 0)
                return 0;

            return _chartViewCount > 0
                ? Math.Clamp(_chartViewStartIndex, 0, Math.Max(0, _currentChartCandles.Count - 1))
                : 0;
        }

        private void AttachChartDragHandlers(Canvas canvas)
        {
            canvas.MouseLeftButtonDown += ChartCanvas_MouseLeftButtonDown;
            canvas.MouseMove += ChartCanvas_MouseMove;
            canvas.MouseLeftButtonUp += ChartCanvas_MouseLeftButtonUp;
        }

        private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                ResetChartViewport();
                DrawFullChart("full reset");
                e.Handled = true;
                return;
            }

            if (_priceChartRenderState == null || sender is not Canvas canvas)
                return;

            _isChartDragSelecting = true;
            _chartDragStartPoint = e.GetPosition(canvas);
            _chartDragSelectionRect = new Rectangle
            {
                Fill = (Brush)FindResource("PaletteSkyBlue"),
                Stroke = (Brush)FindResource("PaletteSkyBlue"),
                StrokeThickness = 1,
                Opacity = 0.22,
                Height = Math.Max(0, canvas.Height)
            };
            Canvas.SetLeft(_chartDragSelectionRect, Math.Min(_chartDragStartPoint.X, _priceChartRenderState.ChartWidth));
            Canvas.SetTop(_chartDragSelectionRect, 0);
            Panel.SetZIndex(_chartDragSelectionRect, 1000);
            canvas.Children.Add(_chartDragSelectionRect);
            canvas.CaptureMouse();
            e.Handled = true;
        }

        private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isChartDragSelecting || _chartDragSelectionRect == null || _priceChartRenderState == null || sender is not Canvas canvas)
                return;

            Point p = e.GetPosition(canvas);
            double startX = Math.Clamp(_chartDragStartPoint.X, 0, _priceChartRenderState.ChartWidth);
            double currentX = Math.Clamp(p.X, 0, _priceChartRenderState.ChartWidth);
            double left = Math.Min(startX, currentX);
            double width = Math.Abs(currentX - startX);

            Canvas.SetLeft(_chartDragSelectionRect, left);
            _chartDragSelectionRect.Width = width;
            _chartDragSelectionRect.Height = Math.Max(0, canvas.Height);
        }

        private void ChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isChartDragSelecting || _priceChartRenderState == null || sender is not Canvas canvas)
                return;

            Point endPoint = e.GetPosition(canvas);
            double startX = Math.Clamp(_chartDragStartPoint.X, 0, _priceChartRenderState.ChartWidth);
            double endX = Math.Clamp(endPoint.X, 0, _priceChartRenderState.ChartWidth);

            if (_chartDragSelectionRect != null)
                canvas.Children.Remove(_chartDragSelectionRect);

            _chartDragSelectionRect = null;
            _isChartDragSelecting = false;
            canvas.ReleaseMouseCapture();

            double width = Math.Abs(endX - startX);
            if (width < 8 || _priceChartRenderState.CandleCount <= 1)
                return;

            int visibleStart = _priceChartRenderState.SourceStartIndex;
            int visibleCount = _priceChartRenderState.CandleCount;
            int first = Math.Clamp((int)Math.Floor(Math.Min(startX, endX) / Math.Max(1, _priceChartRenderState.Gap)), 0, visibleCount - 1);
            int last = Math.Clamp((int)Math.Floor(Math.Max(startX, endX) / Math.Max(1, _priceChartRenderState.Gap)), 0, visibleCount - 1);
            int selectedCount = last - first + 1;
            if (selectedCount < MinChartDragCandleCount)
                selectedCount = Math.Min(MinChartDragCandleCount, visibleCount);

            _chartViewStartIndex = visibleStart + first;
            _chartViewCount = Math.Clamp(selectedCount, 1, _currentChartCandles.Count - _chartViewStartIndex);
            DrawFullChart($"range selection {_chartViewCount}bars");
            e.Handled = true;
        }

        private bool TryUpdateLastChartVisual(ChartCandle candle)
        {
            if (_priceChartRenderState == null ||
                _volumeChartRenderState == null ||
                _lastCandleWick == null ||
                _lastCandleBody == null ||
                _lastVolumeBar == null ||
                _currentChartCandles.Count == 0)
            {
                return false;
            }

            bool markerMissing = _currentPriceMarkerLine == null ||
                _currentPriceMarkerLabel == null ||
                _currentPriceMarkerText == null;
            if (markerMissing && ResolveSelectedCurrentPrice() > 0)
                return false;

            ChartRenderState priceState = _priceChartRenderState;
            int renderedLastIndex = priceState.SourceStartIndex + priceState.CandleCount - 1;
            if (renderedLastIndex != _currentChartCandles.Count - 1)
            {
                if (candle.Close > priceState.Max || candle.Close < priceState.Min)
                    return false;

                UpdateCurrentPriceMarkerVisual(priceState, candle.Close);
                return true;
            }

            if (candle.High > priceState.Max || candle.Low < priceState.Min)
                return false;

            ChartRenderState volumeState = _volumeChartRenderState;

            double priceRange = Math.Max(1, priceState.Max - priceState.Min);
            double x = (priceState.CandleCount - 1) * priceState.Gap + (priceState.Gap - priceState.ItemWidth) / 2.0;
            double centerX = x + priceState.ItemWidth / 2;
            double yHigh = (priceState.Max - candle.High) / priceRange * (priceState.Height - 4) + 2;
            double yLow = (priceState.Max - candle.Low) / priceRange * (priceState.Height - 4) + 2;
            double yOpen = (priceState.Max - candle.Open) / priceRange * (priceState.Height - 4) + 2;
            double yClose = (priceState.Max - candle.Close) / priceRange * (priceState.Height - 4) + 2;
            Brush upDown = candle.Close >= candle.Open ? _upColorBrush : _downColorBrush;

            _lastCandleWick.X1 = centerX;
            _lastCandleWick.X2 = centerX;
            _lastCandleWick.Y1 = yHigh;
            _lastCandleWick.Y2 = yLow;
            _lastCandleWick.Stroke = upDown;

            _lastCandleBody.Width = priceState.ItemWidth;
            _lastCandleBody.Height = Math.Max(1, Math.Abs(yClose - yOpen));
            _lastCandleBody.Fill = upDown;
            Canvas.SetLeft(_lastCandleBody, x);
            Canvas.SetTop(_lastCandleBody, Math.Min(yOpen, yClose));

            UpdateCurrentPriceMarkerVisual(priceState, candle.Close);

            double volumeScale = Math.Max(1, Math.Max(volumeState.MaxVolume, candle.Volume));
            double volumeBarH = (double)candle.Volume / volumeScale * (volumeState.Height - 2);
            double volumeX = (volumeState.CandleCount - 1) * volumeState.Gap + (volumeState.Gap - volumeState.ItemWidth) / 2.0;
            _lastVolumeBar.Width = volumeState.ItemWidth;
            _lastVolumeBar.Height = Math.Max(1, volumeBarH);
            _lastVolumeBar.Fill = upDown;
            Canvas.SetLeft(_lastVolumeBar, volumeX);
            Canvas.SetTop(_lastVolumeBar, volumeState.Height - _lastVolumeBar.Height);

            return true;
        }

        private void UpdateCurrentPriceMarkerVisual(ChartRenderState priceState, double currentPrice)
        {
            if (currentPrice <= 0 ||
                _currentPriceMarkerLine == null ||
                _currentPriceMarkerLabel == null ||
                _currentPriceMarkerText == null)
            {
                return;
            }

            double range = Math.Max(1, priceState.Max - priceState.Min);
            double markerY = (priceState.Max - currentPrice) / range * (priceState.Height - 4) + 2;
            markerY = Math.Max(1, Math.Min(priceState.Height - 1, markerY));
            Brush markerBrush = ResolveHogaBrushByKrxPrevClose((long)Math.Round(currentPrice));

            _currentPriceMarkerLine.Y1 = markerY;
            _currentPriceMarkerLine.Y2 = markerY;
            _currentPriceMarkerLine.Stroke = markerBrush;
            _currentPriceMarkerLabel.BorderBrush = markerBrush;
            Canvas.SetTop(_currentPriceMarkerLabel, Math.Max(0, Math.Min(priceState.Height - 18, markerY - 9)));
            _currentPriceMarkerText.Text = currentPrice.ToString("N0");
            _currentPriceMarkerText.Foreground = markerBrush;
        }

        private static string BuildMinuteBucketTime(string tradeTimeText, int minute)
        {
            DateTime now = DateTime.Now;
            string digits = new([.. (tradeTimeText ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 14)
            {
                string full = digits[..14];
                if (DateTime.TryParseExact(full, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out DateTime parsed))
                    now = parsed;
            }
            else if (digits.Length >= 6)
            {
                string hms = digits[..6];
                if (int.TryParse(hms[..2], out int hour) &&
                    int.TryParse(hms.Substring(2, 2), out int minuteValue) &&
                    int.TryParse(hms.Substring(4, 2), out int second) &&
                    hour >= 0 && hour < 24 &&
                    minuteValue >= 0 && minuteValue < 60 &&
                    second >= 0 && second < 60)
                {
                    now = now.Date.Add(new TimeSpan(hour, minuteValue, second));
                }
            }

            int interval = Math.Max(1, minute);
            int totalMinutes = now.Hour * 60 + now.Minute;
            int bucketTotalMinutes = totalMinutes - (totalMinutes % interval);
            DateTime bucket = now.Date.AddMinutes(bucketTotalMinutes);
            return bucket.ToString("yyyyMMddHHmmss");
        }

        private static bool IsMinuteChartPeriod(ChartPeriod period)
        {
            return period == ChartPeriod.Minute1 ||
                period == ChartPeriod.Minute3 ||
                period == ChartPeriod.Minute5 ||
                period == ChartPeriod.Minute10 ||
                period == ChartPeriod.Minute15 ||
                period == ChartPeriod.Minute30 ||
                period == ChartPeriod.Minute60 ||
                period == ChartPeriod.Minute120;
        }

        private static bool IsCalendarChartPeriod(ChartPeriod period)
        {
            return period == ChartPeriod.Daily ||
                period == ChartPeriod.Weekly ||
                period == ChartPeriod.Monthly;
        }

        private static int ResolveMinuteChartInterval(ChartPeriod period)
        {
            return period switch
            {
                ChartPeriod.Minute1 => 1,
                ChartPeriod.Minute3 => 3,
                ChartPeriod.Minute5 => 5,
                ChartPeriod.Minute10 => 10,
                ChartPeriod.Minute15 => 15,
                ChartPeriod.Minute30 => 30,
                ChartPeriod.Minute60 => 60,
                ChartPeriod.Minute120 => 120,
                _ => 0
            };
        }

        private int ResolveCurrentChartCandleCount(ChartPeriod period)
        {
            int baseCount = ResolveChartCandleCount(period);
            if (!IsMinuteChartPeriod(period))
                return baseCount;

            return Math.Max(1, baseCount + Math.Max(0, _chartAdditionalCandleCount));
        }

        private static int ResolveChartCandleCount(ChartPeriod period)
        {
            return period switch
            {
                ChartPeriod.Daily => 120,
                ChartPeriod.Weekly => 100,
                ChartPeriod.Monthly => 60,
                _ when IsMinuteChartPeriod(period) => MinuteChartCandleCount,
                _ => 120
            };
        }

        private static int ResolveChartRealtimeSnapToLatestDistance(ChartPeriod period)
        {
            return period switch
            {
                ChartPeriod.Minute1 => OneMinuteChartRealtimeSnapToLatestDistance,
                _ when IsMinuteChartPeriod(period) => MinuteChartRealtimeSnapToLatestDistance,
                _ => ChartRealtimeSnapToLatestDistance
            };
        }

        private static int ResolveChartFileCacheRetainCount(ChartPeriod period)
        {
            return period switch
            {
                ChartPeriod.Daily => 600,
                ChartPeriod.Weekly => 300,
                ChartPeriod.Monthly => 180,
                _ => ResolveChartCandleCount(period)
            };
        }

        private static string FormatChartPeriodLabel(ChartPeriod period)
        {
            return period switch
            {
                ChartPeriod.Minute1 => "1min",
                ChartPeriod.Minute3 => "3min",
                ChartPeriod.Minute5 => "5min",
                ChartPeriod.Minute10 => "10min",
                ChartPeriod.Minute15 => "15min",
                ChartPeriod.Minute30 => "30min",
                ChartPeriod.Minute60 => "60min",
                ChartPeriod.Minute120 => "120min",
                ChartPeriod.Daily => "Day",
                ChartPeriod.Weekly => "Week",
                ChartPeriod.Monthly => "Month",
                _ => period.ToString()
            };
        }

        private static string BuildCalendarChartBucketDate(ChartPeriod period, DateTime now)
        {
            return period switch
            {
                ChartPeriod.Weekly => GetWeekStart(now).ToString("yyyyMMdd"),
                ChartPeriod.Monthly => new DateTime(now.Year, now.Month, 1).ToString("yyyyMMdd"),
                _ => now.ToString("yyyyMMdd")
            };
        }

        private static bool IsSameCalendarChartBucket(string chartDate, ChartPeriod period, DateTime now)
        {
            if (!TryParseChartDate(chartDate, out DateTime parsed))
                return IsSameChartDate(chartDate, BuildCalendarChartBucketDate(period, now));

            return period switch
            {
                ChartPeriod.Weekly => GetWeekStart(parsed) == GetWeekStart(now),
                ChartPeriod.Monthly => parsed.Year == now.Year && parsed.Month == now.Month,
                _ => parsed.Date == now.Date
            };
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return date.Date.AddDays(-diff);
        }

        private static bool TryParseChartDate(string chartDate, out DateTime date)
        {
            string digits = new([.. (chartDate ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 8)
                return DateTime.TryParseExact(digits[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date);

            date = default;
            return false;
        }

        private static bool IsSameChartDate(string chartDate, string yyyymmdd)
        {
            string normalized = new([.. (chartDate ?? string.Empty).Where(char.IsDigit)]);
            string target = new([.. (yyyymmdd ?? string.Empty).Where(char.IsDigit)]);
            if (target.Length >= 14 && normalized.Length >= 12)
                return string.Equals(normalized[..Math.Min(12, normalized.Length)], target[..12], StringComparison.Ordinal);
            if (normalized.Length >= 8)
                normalized = normalized[..8];
            if (target.Length >= 8)
                target = target[..8];
            return string.Equals(normalized, target, StringComparison.Ordinal);
        }

        private void DrawRightPriceAxis(Canvas canvas, double chartW, double axisWidth, double h, double min, double max, double tick)
        {
            double rightX = chartW + axisWidth - 1;
            canvas.Children.Add(new Line
            {
                X1 = rightX,
                X2 = rightX,
                Y1 = 0,
                Y2 = h,
                Stroke = _whiteBrush,
                StrokeThickness = 1,
                Opacity = 0.4
            });

            int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                double y = i * (h / steps);
                double raw = max - ((max - min) * i / steps);
                double snapped = Math.Round(raw / tick) * tick;

                canvas.Children.Add(new Line
                {
                    X1 = chartW + 4,
                    X2 = chartW + 10,
                    Y1 = y,
                    Y2 = y,
                    Stroke = _whiteBrush,
                    StrokeThickness = 1,
                    Opacity = 0.6
                });

                var label = new TextBlock
                {
                    Text = snapped.ToString("N0"),
                    FontSize = 11,
                    Foreground = _whiteBrush,
                    Opacity = 0.9
                };
                Canvas.SetLeft(label, chartW + 14);
                Canvas.SetTop(label, Math.Max(0, Math.Min(h - 16, y - 8)));
                canvas.Children.Add(label);
            }
        }

        private void DrawCurrentPriceMarker(Canvas canvas, double chartW, double rightPadding, double axisWidth, double h, double min, double max, double currentPrice)
        {
            if (currentPrice <= 0 || double.IsNaN(currentPrice) || double.IsInfinity(currentPrice))
                return;

            double range = Math.Max(1, max - min);
            double y = (max - currentPrice) / range * (h - 4) + 2;
            y = Math.Max(1, Math.Min(h - 1, y));
            Brush markerBrush = ResolveHogaBrushByKrxPrevClose((long)Math.Round(currentPrice));

            var markerLine = new Line
            {
                X1 = 0,
                X2 = chartW + rightPadding + 10,
                Y1 = y,
                Y2 = y,
                Stroke = markerBrush,
                StrokeThickness = 1,
                Opacity = 0.75
            };
            canvas.Children.Add(markerLine);
            _currentPriceMarkerLine = markerLine;

            var markerText = new TextBlock
            {
                Text = currentPrice.ToString("N0"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = markerBrush
            };
            var label = new Border
            {
                Background = (Brush)FindResource("BgPanelBrush"),
                BorderBrush = markerBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
                Child = markerText
            };

            Canvas.SetLeft(label, chartW + rightPadding + 12);
            Canvas.SetTop(label, Math.Max(0, Math.Min(h - 18, y - 9)));
            canvas.Children.Add(label);
            _currentPriceMarkerLabel = label;
            _currentPriceMarkerText = markerText;
        }

        private void DrawMovingAverage(Canvas canvas, int visibleCount, int visibleStartIndex, int period, Brush color, double w, double h, double min, double max)
        {
            if (_currentChartCandles.Count < period || visibleCount <= 0)
                return;

            double range = Math.Max(1, max - min);
            double gap = w / visibleCount;
            var points = new PointCollection();
            double sum = 0;

            int visibleEndIndex = Math.Min(_currentChartCandles.Count - 1, visibleStartIndex + visibleCount - 1);
            for (int i = 0; i < _currentChartCandles.Count; i++)
            {
                sum += _currentChartCandles[i].Close;
                if (i >= period)
                    sum -= _currentChartCandles[i - period].Close;
                if (i < period - 1)
                    continue;
                if (i < visibleStartIndex)
                    continue;
                if (i > visibleEndIndex)
                    break;

                double avg = sum / period;
                double x = (i - visibleStartIndex) * gap + gap / 2;
                double y = (max - avg) / range * (h - 4) + 2;
                points.Add(new Point(x, y));
            }

            var line = new Polyline
            {
                Stroke = color,
                StrokeThickness = 1.4,
                Points = points
            };
            canvas.Children.Add(line);
        }

        private void DrawPredayRangeBreakoutSignals(Canvas canvas, int visibleCount, int visibleStartIndex, double w, double h, double min, double max)
        {
            if (_currentChartDataPeriod is not (ChartPeriod.Minute3 or ChartPeriod.Minute5) ||
                _currentChartCandles.Count < 4 ||
                visibleCount <= 0)
            {
                return;
            }

            double range = Math.Max(1, max - min);
            double gap = w / visibleCount;
            int visibleEndIndex = Math.Min(_currentChartCandles.Count - 1, visibleStartIndex + visibleCount - 1);

            var tradingDates = _currentChartCandles
                .Select(c => ExtractChartTradingDate(c.Date))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var previousDayRangeByDate = new Dictionary<string, (double High, double Low)>(StringComparer.Ordinal);
            var dayOpenByDate = new Dictionary<string, double>(StringComparer.Ordinal);

            for (int i = 1; i < tradingDates.Count; i++)
            {
                string date = tradingDates[i];
                string previousDate = tradingDates[i - 1];
                List<ChartCandle> previousDayCandles = [.. _currentChartCandles
                    .Where(c => ExtractChartTradingDate(c.Date) == previousDate)];

                if (previousDayCandles.Count == 0)
                    continue;

                previousDayRangeByDate[date] = (previousDayCandles.Max(c => c.High), previousDayCandles.Min(c => c.Low));
            }

            foreach (ChartCandle candle in _currentChartCandles)
            {
                string date = ExtractChartTradingDate(candle.Date);
                if (string.IsNullOrWhiteSpace(date) || dayOpenByDate.ContainsKey(date) || candle.Open <= 0)
                    continue;

                dayOpenByDate[date] = candle.Open;
            }

            for (int sourceIndex = Math.Max(2, visibleStartIndex); sourceIndex <= visibleEndIndex; sourceIndex++)
            {
                ChartCandle candle = _currentChartCandles[sourceIndex];
                string date = ExtractChartTradingDate(candle.Date);
                if (!dayOpenByDate.TryGetValue(date, out double dayOpen) ||
                    !previousDayRangeByDate.TryGetValue(date, out (double High, double Low) previousRange))
                {
                    continue;
                }

                double breakoutLine = dayOpen + (previousRange.High - previousRange.Low) * 0.5;
                double previousClose = _currentChartCandles[sourceIndex - 1].Close;
                if (previousClose > breakoutLine || candle.Close <= breakoutLine)
                    continue;

                double rsi2 = CalculateRsi(sourceIndex, 2);
                if (rsi2 <= 50)
                    continue;

                int visibleIndex = sourceIndex - visibleStartIndex;
                double centerX = visibleIndex * gap + gap / 2;
                double arrowPrice = Math.Max(1, candle.Low * 0.9);
                double arrowY = (max - arrowPrice) / range * (h - 4) + 2;
                DrawWhiteUpArrow(canvas, centerX, Math.Max(0, Math.Min(h - 14, arrowY)));
            }
        }

        private static void DrawWhiteUpArrow(Canvas canvas, double centerX, double topY)
        {
            var triangle = new Polygon
            {
                Fill = Brushes.White,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Points = new PointCollection
                {
                    new(centerX, topY),
                    new(centerX - 6, topY + 10),
                    new(centerX + 6, topY + 10)
                }
            };
            canvas.Children.Add(triangle);
        }

        private double CalculateRsi(int sourceIndex, int period)
        {
            if (sourceIndex < period)
                return 50;

            double gain = 0;
            double loss = 0;
            for (int i = sourceIndex - period + 1; i <= sourceIndex; i++)
            {
                double change = _currentChartCandles[i].Close - _currentChartCandles[i - 1].Close;
                if (change > 0)
                    gain += change;
                else
                    loss -= change;
            }

            if (loss <= 0)
                return gain > 0 ? 100 : 50;

            double rs = gain / loss;
            return 100 - 100 / (1 + rs);
        }

        private static string ExtractChartTradingDate(string chartDate)
        {
            string digits = new([.. (chartDate ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 8 ? digits[..8] : string.Empty;
        }

        private void DrawVolumeChart(List<ChartCandle> candles)
        {
            if (candles.Count == 0)
            {
                VolumeChartHost.Children.Clear();
                _volumeChartRenderState = null;
                _lastVolumeBar = null;
                return;
            }

            VolumeChartHost.Children.Clear();
            var canvas = new Canvas();
            VolumeChartHost.Children.Add(canvas);
            _volumeChartRenderState = null;
            _lastVolumeBar = null;

            VolumeChartHost.UpdateLayout();
            double w = Math.Max(100, VolumeChartHost.ActualWidth - 2);
            double h = Math.Max(40, VolumeChartHost.ActualHeight - 2);
            canvas.Width = w;
            canvas.Height = h;
            const double axisWidth = 68;
            double chartW = Math.Max(40, w - axisWidth - ChartRightPadding);

            long maxVol = Math.Max(1, candles.Max(c => c.Volume));
            int visibleStartIndex = GetVisibleChartStartIndex();
            double maxVolumeMa = ResolveVisibleVolumeMovingAverageMax(candles.Count, visibleStartIndex, 5, 20, 60);
            maxVol = Math.Max(maxVol, (long)Math.Ceiling(maxVolumeMa));
            double barW = Math.Max(1, chartW / candles.Count * 0.62);
            double gap = chartW / candles.Count;

            for (int i = 0; i < candles.Count; i++)
            {
                ChartCandle c = candles[i];
                double x = i * gap + (gap - barW) / 2.0;
                double barH = (double)c.Volume / maxVol * (h - 2);
                var bar = new Rectangle
                {
                    Width = barW,
                    Height = c.Volume > 0 ? Math.Max(2, barH) : 1,
                    Fill = c.Close >= c.Open ? _upColorBrush : _downColorBrush,
                    Opacity = 0.8
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, h - bar.Height);
                canvas.Children.Add(bar);

                if (i == candles.Count - 1)
                    _lastVolumeBar = bar;
            }

            DrawVolumeMovingAverage(canvas, candles.Count, visibleStartIndex, 5, (Brush)FindResource("Ma5Brush"), chartW, h, maxVol, 1.45);
            DrawVolumeMovingAverage(canvas, candles.Count, visibleStartIndex, 20, (Brush)FindResource("Ma20Brush"), chartW, h, maxVol, 1.25);
            DrawVolumeMovingAverage(canvas, candles.Count, visibleStartIndex, 60, (Brush)FindResource("Ma60Brush"), chartW, h, maxVol, 1.25);
            DrawVolumeMaLegend(canvas);
            DrawRightVolumeAxis(canvas, chartW + ChartRightPadding, axisWidth, h, maxVol);
            _volumeChartRenderState = new ChartRenderState(candles.Count, visibleStartIndex, chartW, h, 0, 0, gap, barW, maxVol, 0);
        }

        private double ResolveVisibleVolumeMovingAverageMax(int visibleCount, int visibleStartIndex, params int[] periods)
        {
            if (_currentChartCandles.Count == 0 || visibleCount <= 0 || periods.Length == 0)
                return 0;

            double max = 0;
            int visibleEndIndex = Math.Min(_currentChartCandles.Count - 1, visibleStartIndex + visibleCount - 1);
            foreach (int period in periods.Where(p => p > 0))
            {
                long sum = 0;
                for (int i = 0; i < _currentChartCandles.Count; i++)
                {
                    sum += Math.Max(0, _currentChartCandles[i].Volume);
                    if (i >= period)
                        sum -= Math.Max(0, _currentChartCandles[i - period].Volume);
                    if (i < period - 1)
                        continue;
                    if (i < visibleStartIndex)
                        continue;
                    if (i > visibleEndIndex)
                        break;

                    max = Math.Max(max, sum / (double)period);
                }
            }

            return max;
        }

        private void DrawVolumeMovingAverage(Canvas canvas, int visibleCount, int visibleStartIndex, int period, Brush color, double w, double h, long maxVol, double strokeThickness)
        {
            if (_currentChartCandles.Count < period || visibleCount <= 0 || maxVol <= 0)
                return;

            double gap = w / visibleCount;
            var points = new PointCollection();
            long sum = 0;
            int visibleEndIndex = Math.Min(_currentChartCandles.Count - 1, visibleStartIndex + visibleCount - 1);

            for (int i = 0; i < _currentChartCandles.Count; i++)
            {
                sum += Math.Max(0, _currentChartCandles[i].Volume);
                if (i >= period)
                    sum -= Math.Max(0, _currentChartCandles[i - period].Volume);
                if (i < period - 1)
                    continue;
                if (i < visibleStartIndex)
                    continue;
                if (i > visibleEndIndex)
                    break;

                double avg = sum / (double)period;
                double x = (i - visibleStartIndex) * gap + gap / 2;
                double y = h - avg / maxVol * (h - 2);
                points.Add(new Point(x, Math.Max(1, Math.Min(h - 1, y))));
            }

            if (points.Count < 2)
                return;

            canvas.Children.Add(new Polyline
            {
                Stroke = color,
                StrokeThickness = strokeThickness,
                Opacity = 0.95,
                Points = points
            });
        }

        private void DrawVolumeMaLegend(Canvas canvas)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 2, 0, 0),
                IsHitTestVisible = false
            };

            AddVolumeMaLegendText(panel, "Vol5", (Brush)FindResource("Ma5Brush"));
            AddVolumeMaLegendText(panel, "Vol20", (Brush)FindResource("Ma20Brush"));
            AddVolumeMaLegendText(panel, "Vol60", (Brush)FindResource("Ma60Brush"));

            Canvas.SetLeft(panel, 4);
            Canvas.SetTop(panel, 2);
            canvas.Children.Add(panel);
        }

        private static void AddVolumeMaLegendText(Panel panel, string text, Brush brush)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0)
            });
        }

        private void DrawRightVolumeAxis(Canvas canvas, double chartW, double axisWidth, double h, long maxVol)
        {
            double rightX = chartW + axisWidth - 1;
            canvas.Children.Add(new Line
            {
                X1 = rightX,
                X2 = rightX,
                Y1 = 0,
                Y2 = h,
                Stroke = _whiteBrush,
                StrokeThickness = 1,
                Opacity = 0.35
            });

            const int steps = 5;
            for (int i = 0; i <= steps; i++)
            {
                double y = i * (h / steps);
                long value = (long)Math.Round(maxVol * (steps - i) / (double)steps);

                canvas.Children.Add(new Line
                {
                    X1 = chartW + 4,
                    X2 = chartW + 10,
                    Y1 = y,
                    Y2 = y,
                    Stroke = _whiteBrush,
                    StrokeThickness = 1,
                    Opacity = 0.45
                });

                var label = new TextBlock
                {
                    Text = FormatAxisNumber(value),
                    FontSize = 10,
                    Foreground = _whiteBrush,
                    Opacity = 0.8
                };
                Canvas.SetLeft(label, chartW + 14);
                Canvas.SetTop(label, Math.Max(0, Math.Min(h - 14, y - 7)));
                canvas.Children.Add(label);
            }
        }

        private static string FormatAxisNumber(long value)
        {
            if (value >= 100_000_000)
                return $"{value / 100_000_000d:0.#}B KRW";
            if (value >= 10_000)
                return $"{value / 10_000d:0.#}K";
            return value.ToString("N0");
        }

        private static string FormatMillionWonUnit(long value)
        {
            if (value <= 0)
                return "-";

            decimal hundredMillion = value / 100m;
            if (hundredMillion >= 10m)
                return $"{hundredMillion:N1}B KRW";

            return $"{value:N0}M KRW";
        }

        private enum ChartPeriod
        {
            Minute1,
            Minute3,
            Minute5,
            Minute10,
            Minute15,
            Minute30,
            Minute60,
            Minute120,
            Daily,
            Weekly,
            Monthly
        }

        private sealed class ChartCandle
        {
            public string Date { get; set; } = string.Empty;
            public double Open { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public double Close { get; set; }
            public long Volume { get; set; }
        }

        private sealed record ChartRenderState(
            int CandleCount,
            int SourceStartIndex,
            double ChartWidth,
            double Height,
            double Min,
            double Max,
            double Gap,
            double ItemWidth,
            long MaxVolume,
            double AxisWidth);

        private readonly record struct ChartCacheKey(string Code, bool UseNxtMarket, ChartPeriod Period);

        private sealed class ChartCacheEntry
        {
            public List<ChartCandle> Candles { get; set; } = [];
            public DateTime CachedAt { get; set; }
            public long LastAccess { get; set; }
        }

        private sealed record ChartPreloadStock(string Code, bool UseNxtMarket);

        private static ChartCandle ToChartCandle(DailyCandle c) => new()
        {
            Date = c.Date,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume
        };


    }
}
