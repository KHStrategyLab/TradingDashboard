using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TradingDashboard.Models;
using TradingDashboard.Services.Trading;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _balanceRequestCts;
        private Point? _balanceGridDragStart;
        private double _balanceGridDragStartOffset;
        private bool _balanceGridSelectionSyncing;

        private async void BalanceRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshBalanceAsync("manual");
        }

        private async void BalanceStatusText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
                return;

            e.Handled = true;
            await VerifyBalanceAgainstMtsAsync();
        }

        private void BalanceHoldingsDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                return;

            ScrollViewer? scrollViewer = GetBalanceHoldingsScrollViewer();
            if (scrollViewer is null)
                return;

            double direction = e.Delta > 0 ? -1 : 1;
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + direction * 90);
            e.Handled = true;
        }

        private void BalanceHoldingsDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;

            ScrollViewer? scrollViewer = GetBalanceHoldingsScrollViewer();
            if (scrollViewer is null)
                return;

            _balanceGridDragStart = e.GetPosition(BalanceHoldingsHorizontalScrollViewer);
            _balanceGridDragStartOffset = scrollViewer.HorizontalOffset;
            BalanceHoldingsHorizontalScrollViewer.CaptureMouse();
            BalanceHoldingsHorizontalScrollViewer.Cursor = Cursors.SizeWE;
            e.Handled = true;
        }

        private void BalanceHoldingsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_balanceGridDragStart is not Point dragStart)
                return;

            ScrollViewer? scrollViewer = GetBalanceHoldingsScrollViewer();
            if (scrollViewer is null)
                return;

            Point current = e.GetPosition(BalanceHoldingsHorizontalScrollViewer);
            scrollViewer.ScrollToHorizontalOffset(_balanceGridDragStartOffset - (current.X - dragStart.X));
            e.Handled = true;
        }

        private void BalanceHoldingsDataGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || _balanceGridDragStart is null)
                return;

            _balanceGridDragStart = null;
            BalanceHoldingsHorizontalScrollViewer.ReleaseMouseCapture();
            BalanceHoldingsHorizontalScrollViewer.Cursor = null;
            e.Handled = true;
        }

        private async Task RefreshBalanceAsync(string reason)
        {
            _balanceRequestCts?.Cancel();
            _balanceRequestCts?.Dispose();
            _balanceRequestCts = new CancellationTokenSource();
            CancellationToken cancellationToken = _balanceRequestCts.Token;

            BalanceRefreshButton.IsEnabled = false;
            BalanceStatusText.Text = $"Balance loading... {reason}";

            try
            {
                KiwoomBalanceSnapshot snapshot = await LoadMergedBalanceSnapshotAsync(cancellationToken)
                    .ConfigureAwait(true);

                SyncManualPositionLedger(snapshot.Holdings);
                _balanceHoldings.Clear();
                foreach (KiwoomHolding holding in snapshot.Holdings.OrderByDescending(x => Math.Abs(x.EvaluationAmount)))
                    _balanceHoldings.Add(DecorateHoldingPositionTag(holding));
                await AddBalanceHoldingsToRecentViewsAsync(snapshot.Holdings, cancellationToken);
                UpdateStrategyProgressRows();

                BalanceTotalPurchaseText.Text = $"{snapshot.TotalPurchaseAmount:N0}";
                BalanceTotalEvaluationText.Text = $"{snapshot.TotalEvaluationAmount:N0}";
                BalanceTotalProfitText.Text = $"{snapshot.TotalEvaluationProfit:N0}";
                BalanceTotalProfitRateText.Text = $"{snapshot.TotalProfitRate:N2}%";
                await RefreshRealizedProfitAsync(cancellationToken).ConfigureAwait(true);
                BalanceStatusText.Text = $"kt00018 {snapshot.QueryMarket} / {snapshot.Holdings.Count} items / {snapshot.CapturedAt:HH:mm:ss}";
                AppendLog($"balance refreshed: {snapshot.SourceApi} / {snapshot.QueryMarket} / {snapshot.Holdings.Count}items / {reason}");
            }
            catch (OperationCanceledException)
            {
                BalanceStatusText.Text = "Balance refresh canceled";
            }
            catch (Exception ex)
            {
                BalanceStatusText.Text = "Balance refresh failed";
                AppendLog($"balance refresh error: {ex.GetType().Name} / {ex.Message}");
            }
            finally
            {
                BalanceRefreshButton.IsEnabled = true;
            }
        }

        private async Task<KiwoomBalanceSnapshot> LoadMergedBalanceSnapshotAsync(CancellationToken cancellationToken)
        {
            KiwoomBalanceSnapshot krx = await _tradingClient
                .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketKrx, cancellationToken)
                .ConfigureAwait(true);

            List<KiwoomHolding> holdings = [.. krx.Holdings];
            List<string> sources = [$"{krx.SourceApi}:{krx.QueryMarket}"];
            HashSet<string> knownCodes = [.. holdings
                .Select(item => NormalizeStockCode(item.StockCode))
                .Where(code => !string.IsNullOrWhiteSpace(code))];

            try
            {
                using CancellationTokenSource nxtCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                nxtCts.CancelAfter(TimeSpan.FromSeconds(3));
                KiwoomBalanceSnapshot nxt = await _tradingClient
                    .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketNxt, nxtCts.Token)
                    .ConfigureAwait(true);
                IReadOnlyList<KiwoomHolding> missingNxtHoldings = [.. nxt.Holdings
                    .Where(item => item.HoldingQuantity > 0 && !knownCodes.Contains(NormalizeStockCode(item.StockCode)))];
                if (missingNxtHoldings.Count > 0)
                {
                    holdings.AddRange(missingNxtHoldings);
                    foreach (KiwoomHolding item in missingNxtHoldings)
                        knownCodes.Add(NormalizeStockCode(item.StockCode));
                    sources.Add($"{nxt.SourceApi}:{nxt.QueryMarket}-missing");
                    AppendLog($"balance NXT snapshot supplemented missing holdings only: {missingNxtHoldings.Count}items");
                }
                else
                {
                    AppendLog($"balance NXT snapshot reference only: {nxt.Holdings.Count}items / duplicate merge blocked");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendLog($"balance NXT supplement skipped: {ex.GetType().Name} / {ex.Message}");
            }

            try
            {
                using CancellationTokenSource executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                executionCts.CancelAfter(TimeSpan.FromSeconds(3));
                KiwoomBalanceSnapshot executionKrx = await _tradingClient
                    .GetExecutionBalanceAsync(KiwoomTradingConstants.MarketKrx, executionCts.Token)
                    .ConfigureAwait(true);
                IReadOnlyList<KiwoomHolding> missingExecutionHoldings = [.. executionKrx.Holdings
                    .Where(item => item.HoldingQuantity > 0 && !knownCodes.Contains(NormalizeStockCode(item.StockCode)))];
                if (missingExecutionHoldings.Count > 0)
                {
                    holdings.AddRange(missingExecutionHoldings);
                    sources.Add($"{executionKrx.SourceApi}:{executionKrx.QueryMarket}-missing");
                    AppendLog($"balance kt00005 supplemented missing holdings: {missingExecutionHoldings.Count}items");
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;

                AppendLog("balance kt00005 supplement skipped: timeout 3s");
            }
            catch (Exception ex)
            {
                AppendLog($"balance kt00005 supplement skipped: {ex.GetType().Name} / {ex.Message}");
            }

            IReadOnlyList<KiwoomHolding> mergedHoldings = MergeBalanceHoldings(holdings);
            mergedHoldings = await ApplyNxtBalanceOverlayAsync(mergedHoldings, cancellationToken)
                .ConfigureAwait(true);
            long totalPurchase = mergedHoldings.Sum(item => Math.Max(0, item.PurchaseAmount));
            long totalEvaluation = mergedHoldings.Sum(item => Math.Max(0, item.EvaluationAmount));
            long totalProfit = mergedHoldings.Sum(item => item.EvaluationProfit);
            decimal totalProfitRate = totalPurchase > 0
                ? totalProfit / (decimal)totalPurchase * 100m
                : krx.TotalProfitRate;

            return new KiwoomBalanceSnapshot(
                string.Join("+", sources),
                "KRX baseline + NXT overlay",
                DateTime.Now,
                totalPurchase,
                totalEvaluation,
                totalProfit,
                totalProfitRate,
                mergedHoldings,
                krx.RawBody);
        }

        private async Task<IReadOnlyList<KiwoomHolding>> ApplyNxtBalanceOverlayAsync(
            IReadOnlyList<KiwoomHolding> holdings,
            CancellationToken cancellationToken)
        {
            if (holdings.Count == 0 || (!ShouldUseNxtMarketNow() && !IsNxtFrozenWindow()))
                return holdings;

            List<KiwoomHolding> corrected = new(holdings.Count);
            int eligibleCount = 0;
            int appliedCount = 0;

            foreach (KiwoomHolding holding in holdings)
            {
                string code = NormalizeStockCode(holding.StockCode);
                if (string.IsNullOrWhiteSpace(code) || holding.HoldingQuantity <= 0)
                {
                    corrected.Add(holding);
                    continue;
                }

                try
                {
                    using CancellationTokenSource overlayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    overlayCts.CancelAfter(TimeSpan.FromSeconds(4));

                    if (!await IsBalanceNxtEligibleAsync(code, overlayCts.Token).ConfigureAwait(true))
                    {
                        corrected.Add(holding);
                        continue;
                    }

                    eligibleCount++;

                    (long price, string source) = await FetchNxtBalanceOverlayPriceAsync(code, overlayCts.Token)
                        .ConfigureAwait(true);
                    if (price <= 0)
                    {
                        corrected.Add(holding);
                        continue;
                    }

                    KiwoomHolding revalued = RevalueHoldingWithCurrentPrice(holding, price);
                    corrected.Add(revalued);
                    if (price != holding.CurrentPrice || revalued.EvaluationAmount != holding.EvaluationAmount)
                    {
                        appliedCount++;
                        AppendLog($"balance NXT overlay: {holding.StockName}({code}) / {source} / now {holding.CurrentPrice:N0}->{price:N0} / eval {revalued.EvaluationAmount:N0} / pl {revalued.EvaluationProfit:N0}");
                    }

                    await Task.Delay(120, cancellationToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;

                    corrected.Add(holding);
                    AppendLog($"balance NXT overlay skipped: {holding.StockName}({code}) / timeout 4s");
                }
                catch (Exception ex)
                {
                    corrected.Add(holding);
                    AppendLog($"balance NXT overlay skipped: {holding.StockName}({code}) / {ex.GetType().Name} / {ex.Message}");
                }
            }

            if (eligibleCount > 0)
                AppendLog($"balance NXT overlay completed: eligible {eligibleCount}items / applied {appliedCount}items");

            return corrected;
        }

        private async Task<bool> IsBalanceNxtEligibleAsync(string code, CancellationToken cancellationToken)
        {
            if (_watchStockByCode.TryGetValue(code, out WatchStockItem? watchStock) && watchStock.SupportsNxt)
                return true;

            WatchStockItem? recentStock = _recentViewedStocks
                .FirstOrDefault(item => string.Equals(NormalizeStockCode(item.Code), code, StringComparison.Ordinal));
            if (recentStock?.SupportsNxt == true)
                return true;

            WatchStockItem? enriched = await TrySearchListedStockQuietAsync(code, cancellationToken)
                .ConfigureAwait(true);
            if (enriched == null)
                return false;

            if (_watchStockByCode.TryGetValue(code, out WatchStockItem? existing))
                MergeBalanceStockMetadata(existing, enriched);
            else if (recentStock != null)
                MergeBalanceStockMetadata(recentStock, enriched);

            return enriched.SupportsNxt;
        }

        private async Task<(long Price, string Source)> FetchNxtBalanceOverlayPriceAsync(string code, CancellationToken cancellationToken)
        {
            IReadOnlyList<(string RequestCode, StockStatusMetrics Metrics)> rows = await _kiwoomConditionService
                .GetStockStatusMetricsCompareAsync(code, cancellationToken)
                .ConfigureAwait(true);

            foreach ((string requestCode, StockStatusMetrics metrics) in rows
                .Where(row => row.RequestCode.EndsWith("_AL", StringComparison.OrdinalIgnoreCase) ||
                              row.RequestCode.EndsWith("_NX", StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.RequestCode.EndsWith("_AL", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                long price = ParseLongAbs(metrics.ClosePriceText);
                if (price > 0)
                    return (price, requestCode);
            }

            return (0, string.Empty);
        }

        private static KiwoomHolding RevalueHoldingWithCurrentPrice(KiwoomHolding holding, long currentPrice)
        {
            long quantity = Math.Max(0, holding.HoldingQuantity);
            long purchase = Math.Max(0, holding.PurchaseAmount);
            long evaluation = quantity > 0 && currentPrice > 0
                ? (long)Math.Min(long.MaxValue, quantity * (double)currentPrice)
                : Math.Max(0, holding.EvaluationAmount);
            long profit = purchase > 0 ? evaluation - purchase : holding.EvaluationProfit;
            decimal profitRate = purchase > 0 ? profit / (decimal)purchase * 100m : holding.ProfitRate;

            return holding with
            {
                CurrentPrice = currentPrice,
                EvaluationAmount = evaluation,
                EvaluationProfit = profit,
                ProfitRate = profitRate
            };
        }

        private async Task AddBalanceHoldingsToRecentViewsAsync(IEnumerable<KiwoomHolding> holdings, CancellationToken cancellationToken)
        {
            List<KiwoomHolding> orderedHoldings = [.. (holdings ?? [])
                .Where(item => item.HoldingQuantity > 0 && !string.IsNullOrWhiteSpace(item.StockCode))
                .OrderByDescending(item => Math.Abs(item.EvaluationAmount))];
            int newlyTracked = 0;
            int prepared = 0;

            for (int i = orderedHoldings.Count - 1; i >= 0; i--)
            {
                KiwoomHolding holding = orderedHoldings[i];
                string code = NormalizeStockCode(holding.StockCode);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                WatchStockItem? stock = _watchStockByCode.TryGetValue(code, out WatchStockItem? existing)
                    ? existing
                    : _recentViewedStocks.FirstOrDefault(item => string.Equals(NormalizeStockCode(item.Code), code, StringComparison.Ordinal));

                if (stock == null || !stock.SupportsNxt)
                {
                    WatchStockItem? enriched = await TrySearchListedStockQuietAsync(code, cancellationToken);
                    if (enriched != null)
                    {
                        if (stock == null)
                        {
                            stock = enriched;
                        }
                        else
                        {
                            MergeBalanceStockMetadata(stock, enriched);
                        }
                    }
                }

                stock ??= new WatchStockItem { Code = code };

                if (string.IsNullOrWhiteSpace(stock.Name) && !string.IsNullOrWhiteSpace(holding.StockName))
                    stock.Name = holding.StockName;
                if (stock.CurrentPrice <= 0 && holding.CurrentPrice > 0)
                    ApplyWatchStockDisplayPrice(stock, holding.CurrentPrice, ResolveCachedPriceMarket(stock), "balance tracking");

                if (await EnsureRealtime0BTrackingAsync(stock, "balance", registerImmediately: false))
                    newlyTracked++;
                prepared++;
                AddRecentViewedStock(stock);
            }

            if (prepared == 0)
                return;

            int restored = EnsureBalanceHoldingsTrackedInRealtimeMap();
            await RegisterRealtime0BForCurrentWatchlistAsync();
            AppendLog($"balance holdings tracking ready: {prepared}stocks / {newlyTracked + restored}new / 0B batch");
            StartBalancePriorityChartDataPreload(BuildBalanceFirstStrategyPreloadList([]));

            if (_watchStocks.Count > 0)
                StartStrategyMinuteAutoPreload(BuildBalanceFirstStrategyPreloadList(_watchStocks), immediate: true);
        }

        private int EnsureBalanceHoldingsTrackedInRealtimeMap()
        {
            int added = 0;
            foreach (KiwoomHolding holding in _balanceHoldings
                .Where(item => item.HoldingQuantity > 0 && !string.IsNullOrWhiteSpace(item.StockCode)))
            {
                string code = NormalizeStockCode(holding.StockCode);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                WatchStockItem? recent = _recentViewedStocks
                    .FirstOrDefault(item => string.Equals(NormalizeStockCode(item.Code), code, StringComparison.Ordinal));

                if (_watchStockByCode.TryGetValue(code, out WatchStockItem? existing))
                {
                    if (recent != null)
                        MergeBalanceStockMetadata(existing, recent);
                    if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(holding.StockName))
                        existing.Name = holding.StockName;
                    if (existing.CurrentPrice <= 0 && holding.CurrentPrice > 0)
                        ApplyWatchStockDisplayPrice(existing, holding.CurrentPrice, ResolveCachedPriceMarket(existing), "balance tracking map");
                    continue;
                }

                WatchStockItem stock = recent ?? new WatchStockItem { Code = code };
                if (string.IsNullOrWhiteSpace(stock.Name) && !string.IsNullOrWhiteSpace(holding.StockName))
                    stock.Name = holding.StockName;
                if (stock.CurrentPrice <= 0 && holding.CurrentPrice > 0)
                    ApplyWatchStockDisplayPrice(stock, holding.CurrentPrice, ResolveCachedPriceMarket(stock), "balance tracking map");

                _watchStockByCode[code] = stock;
                added++;
            }

            return added;
        }

        private static void MergeBalanceStockMetadata(WatchStockItem target, WatchStockItem source)
        {
            if (target == null || source == null)
                return;

            target.SupportsNxt |= source.SupportsNxt;
            if (string.IsNullOrWhiteSpace(target.MarketTypeCode))
                target.MarketTypeCode = source.MarketTypeCode;
            if (string.IsNullOrWhiteSpace(target.MarketName))
                target.MarketName = source.MarketName;
            if (string.IsNullOrWhiteSpace(target.ProgramMarketType))
                target.ProgramMarketType = source.ProgramMarketType;
            if (target.LastPrice <= 0)
                target.LastPrice = source.LastPrice;
            if (string.IsNullOrWhiteSpace(target.OrderWarning))
                target.OrderWarning = source.OrderWarning;
            if (string.IsNullOrWhiteSpace(target.AuditInfo))
                target.AuditInfo = source.AuditInfo;
            if (string.IsNullOrWhiteSpace(target.StockState))
                target.StockState = source.StockState;
            if (string.IsNullOrWhiteSpace(target.SectorName))
                target.SectorName = source.SectorName;
        }

        private async Task<WatchStockItem?> TrySearchListedStockQuietAsync(string code, CancellationToken cancellationToken)
        {
            try
            {
                return await _kiwoomConditionService.SearchListedStockAsync(code, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendLog($"balance stock metadata skipped: {code} / {ex.GetType().Name} / {ex.Message}");
                return null;
            }
        }

        private static IReadOnlyList<KiwoomHolding> MergeBalanceHoldings(IEnumerable<KiwoomHolding> holdings)
        {
            return [.. (holdings ?? [])
                .Where(item => item.HoldingQuantity > 0 && !string.IsNullOrWhiteSpace(item.StockCode))
                .GroupBy(item => NormalizeStockCode(item.StockCode), StringComparer.Ordinal)
                .Select(group =>
                {
                    // KRX/NXT 잔고 스냅샷은 같은 계좌 보유를 시장별로 중복 반환할 수 있다.
                    // 같은 종목은 절대 합산하지 않고, 먼저 확보한 기준 행만 평가 오버레이 대상으로 둔다.
                    return group.First();
                })
                .OrderByDescending(item => Math.Abs(item.EvaluationAmount))];
        }

        private void ApplyRealtimePriceToBalanceHolding(string code, long currentPrice, string rawCode)
        {
            code = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(code) || currentPrice <= 0 || _balanceHoldings.Count == 0)
                return;

            bool shouldUseNxtPrice = IsNxtSupportedStock(code) && (ShouldUseNxtMarketNow() || IsNxtFrozenWindow());
            bool isNxtTick = IsNxtRealtimeCode(rawCode);
            if (shouldUseNxtPrice != isNxtTick)
                return;

            for (int i = 0; i < _balanceHoldings.Count; i++)
            {
                KiwoomHolding holding = _balanceHoldings[i];
                if (!string.Equals(NormalizeStockCode(holding.StockCode), code, StringComparison.Ordinal))
                    continue;

                _balanceHoldings[i] = DecorateHoldingPositionTag(RevalueHoldingWithCurrentPrice(holding, currentPrice));
                RefreshBalanceSummaryFromCurrentHoldings();
                return;
            }
        }

        private void RefreshBalanceSummaryFromCurrentHoldings()
        {
            long totalPurchase = _balanceHoldings.Sum(item => Math.Max(0, item.PurchaseAmount));
            long totalEvaluation = _balanceHoldings.Sum(item => Math.Max(0, item.EvaluationAmount));
            long totalProfit = _balanceHoldings.Sum(item => item.EvaluationProfit);
            decimal totalProfitRate = totalPurchase > 0
                ? totalProfit / (decimal)totalPurchase * 100m
                : 0m;

            BalanceTotalPurchaseText.Text = $"{totalPurchase:N0}";
            BalanceTotalEvaluationText.Text = $"{totalEvaluation:N0}";
            BalanceTotalProfitText.Text = $"{totalProfit:N0}";
            BalanceTotalProfitRateText.Text = $"{totalProfitRate:N2}%";
        }

        private async Task RefreshRealizedProfitAsync(CancellationToken cancellationToken)
        {
            try
            {
                KiwoomRealizedProfitSnapshot snapshot = await _tradingClient
                    .GetTodayRealizedProfitAsync(cancellationToken)
                    .ConfigureAwait(true);

                BalanceRealizedProfitText.Text = FormatSignedNumber(snapshot.RealizedProfit);
                BalanceRealizedProfitText.Foreground = snapshot.RealizedProfit > 0
                    ? _upColorBrush
                    : snapshot.RealizedProfit < 0 ? _downColorBrush : _whiteBrush;

                AppendLog($"realized profit refreshed: ka10074 / {snapshot.QueryDate:yyyyMMdd} / pl {snapshot.RealizedProfit:N0} / fee {snapshot.TradeCommission:N0} / tax {snapshot.TradeTax:N0}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                BalanceRealizedProfitText.Text = "-";
                BalanceRealizedProfitText.Foreground = _whiteBrush;
                AppendLog($"realized profit refresh error: {ex.GetType().Name} / {ex.Message}");
            }
        }

        private static string FormatSignedNumber(long value)
        {
            if (value > 0)
                return $"+{value:N0}";

            return value < 0 ? $"-{Math.Abs(value):N0}" : "0";
        }

        private ScrollViewer? GetBalanceHoldingsScrollViewer()
        {
            return BalanceHoldingsHorizontalScrollViewer;
        }

        private async Task VerifyBalanceAgainstMtsAsync()
        {
            BalanceStatusText.Text = "Hidden balance verification running...";
            AppendLog("balance verify started: kt00018 KRX/NXT + kt00005 KRX");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                KiwoomBalanceSnapshot kt00018Krx = await _tradingClient
                    .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketKrx, cts.Token)
                    .ConfigureAwait(true);
                KiwoomBalanceSnapshot kt00018Nxt = await _tradingClient
                    .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketNxt, cts.Token)
                    .ConfigureAwait(true);
                KiwoomBalanceSnapshot kt00005Krx = await _tradingClient
                    .GetExecutionBalanceAsync(KiwoomTradingConstants.MarketKrx, cts.Token)
                    .ConfigureAwait(true);

                var merged = kt00018Krx.Holdings
                    .Concat(kt00018Nxt.Holdings)
                    .GroupBy(x => NormalizeStockCode(x.StockCode), StringComparer.Ordinal)
                    .Select(group => new
                    {
                        Code = group.Key,
                        Name = group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.StockName))?.StockName ?? string.Empty,
                        Quantity = group.First().HoldingQuantity,
                        Orderable = group.First().OrderableQuantity,
                        Evaluation = group.First().EvaluationAmount,
                        Profit = group.First().EvaluationProfit
                    })
                    .OrderByDescending(x => Math.Abs(x.Evaluation))
                    .ToList();

                AppendLog($"balance verify kt00018 KRX: {kt00018Krx.Holdings.Count}items / eval {kt00018Krx.TotalEvaluationAmount:N0} / pl {kt00018Krx.TotalEvaluationProfit:N0} / rate {kt00018Krx.TotalProfitRate:N2}%");
                AppendLog($"balance verify kt00018 NXT: {kt00018Nxt.Holdings.Count}items / eval {kt00018Nxt.TotalEvaluationAmount:N0} / pl {kt00018Nxt.TotalEvaluationProfit:N0} / rate {kt00018Nxt.TotalProfitRate:N2}%");
                AppendLog($"balance verify kt00018 KRX-primary union: {merged.Count}items / eval {merged.Sum(x => x.Evaluation):N0} / pl {merged.Sum(x => x.Profit):N0}");
                AppendLog($"balance verify kt00005 KRX: {kt00005Krx.Holdings.Count}items / eval {kt00005Krx.TotalEvaluationAmount:N0} / pl {kt00005Krx.TotalEvaluationProfit:N0} / rate {kt00005Krx.TotalProfitRate:N2}%");

                foreach (var row in merged.Take(8))
                {
                    AppendLog($"balance verify merged item: {row.Name}({row.Code}) qty {row.Quantity:N0} / able {row.Orderable:N0} / eval {row.Evaluation:N0} / pl {row.Profit:N0}");
                }

                BalanceStatusText.Text = "Hidden balance verification logged";
            }
            catch (Exception ex)
            {
                BalanceStatusText.Text = "Hidden balance verification failed";
                AppendLog($"balance verify error: {ex.GetType().Name} / {ex.Message}");
            }
        }
    }
}
