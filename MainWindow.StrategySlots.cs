using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TradingDashboard.Models;
using TradingDashboard.Services.Strategies;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private readonly StrategySlotRegistry _strategySlotRegistry = StrategySlotRegistry.CreateDefault();
        private readonly System.Collections.ObjectModel.ObservableCollection<StrategyProgressRow> _strategyProgressRows = [];
        private bool _isRevertingLockedStrategyToggle;

        private void InitializeStrategySlots()
        {
            StrategyProgressItemsControl.ItemsSource = _strategyProgressRows;
            UpdateStrategySlotSummary();
            UpdateStrategyControlBoard();
            UpdateStrategyProgressRows();
        }

        private void StrategySlotToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (TryRejectEngineLockedStrategyChange(sender))
                return;

            UpdateStrategySlotSummary();
            UpdateStrategyControlBoard();
            UpdateStrategyProgressRows();
        }

        private void StrategyControlBoard_Changed(object sender, RoutedEventArgs e)
        {
            if (TryRejectEngineLockedStrategyChange(sender))
            {
                SyncPaperTradingPreviewState();
                UpdateStrategyControlBoard();
                return;
            }

            SyncPaperTradingPreviewState();
            UpdateStrategyControlBoard();
            TryStartStrategyMinutePreloadForSelectedStock();
        }

        private void StrategyControlBoardInput_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateStrategyControlBoard();
        }

        private void StrategyProgressFilter_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStrategyProgressRows();
        }

        private void SyncPaperTradingPreviewState()
        {
            if (AutoTradingEnabledToggle == null || VirtualTradingPreviewToggle == null)
                return;

            bool engineStarted = IsStrategyToggleOn(AutoTradingEnabledToggle);
            if (engineStarted && VirtualTradingPreviewToggle.IsChecked == true)
                VirtualTradingPreviewToggle.IsChecked = false;

            VirtualTradingPreviewToggle.IsEnabled = !engineStarted;
        }

        private bool TryRejectEngineLockedStrategyChange(object sender)
        {
            if (_isRevertingLockedStrategyToggle)
                return false;

            if (!IsEngineConfigurationLocked())
                return false;

            if (sender is not ToggleButton toggle || !IsLockedStrategyConfigurationToggle(toggle))
                return false;

            _isRevertingLockedStrategyToggle = true;
            try
            {
                toggle.IsChecked = toggle.IsChecked != true;
            }
            finally
            {
                _isRevertingLockedStrategyToggle = false;
            }

            AppendLog("띵띵: Engine Start 중에는 전략 설정을 변경할 수 없음. 전략은 켜기 전에 설정하라.");
            try
            {
                global::System.Media.SystemSounds.Exclamation.Play();
            }
            catch
            {
                // Some Windows sound schemes may not provide a playable alert.
            }

            return true;
        }

        private bool IsEngineConfigurationLocked() =>
            IsStrategyToggleOn(AutoTradingEnabledToggle);

        private bool IsLockedStrategyConfigurationToggle(ToggleButton toggle) =>
            ReferenceEquals(toggle, StrategySlotBaseCandleChaseToggle) ||
            ReferenceEquals(toggle, StrategySlotPullbackToggle) ||
            ReferenceEquals(toggle, StrategySlotMiddleToggle) ||
            ReferenceEquals(toggle, StrategySlotThemeAssistToggle) ||
            ReferenceEquals(toggle, DuplicateBuyPolicyToggle) ||
            ReferenceEquals(toggle, DuplicateAlertPolicyToggle);

        private void StrategyDetailButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not ButtonBase button ||
                button.Tag is not string tag ||
                !Enum.TryParse(tag, out StrategySlotId slotId))
                return;

            StrategySlotDescriptor? descriptor = _strategySlotRegistry.GetDescriptor(slotId);
            if (descriptor == null)
                return;

            StrategyDetailTitleText.Text = $"{descriptor.Name} · {descriptor.MarketBadgeText}";
            StrategySlotSummaryText.Text = descriptor.Detail;
            StrategyDetailDocumentText.Text = $"문서: {descriptor.DocumentPath}";
            StrategySlotStateText.Text = descriptor.MarketBadgeText;
            StrategySlotStateText.Foreground = descriptor.MarketScope switch
            {
                StrategyMarketScope.KrxOnly => (Brush)FindResource("PalettePink"),
                StrategyMarketScope.NxtOnly => (Brush)FindResource("PaletteSkyBlue"),
                StrategyMarketScope.Sor => (Brush)FindResource("PaletteSkyBlue"),
                StrategyMarketScope.Assist => (Brush)FindResource("PaletteLightYellow"),
                _ => (Brush)FindResource("TextMainBrush")
            };
        }

        private IReadOnlyList<StrategySlotSetting> GetStrategySlotSettings() =>
        [
            new(
                StrategySlotId.BaseCandleChase,
                "SOR 10min MA60 + 3min Breakout",
                IsStrategyToggleOn(StrategySlotBaseCandleChaseToggle)),
            new(
                StrategySlotId.ThreeMinutePullback,
                "SOR 15min MA60 + 5min Breakout",
                IsStrategyToggleOn(StrategySlotPullbackToggle)),
            new(
                StrategySlotId.SorTenMinuteFiveMinuteBreakout,
                "SOR 10min MA60 + 5min Breakout",
                IsStrategyToggleOn(StrategySlotMiddleToggle)),
            new(
                StrategySlotId.ThemeDisclosureAssist,
                "Theme / Disclosure Assist",
                IsStrategyToggleOn(StrategySlotThemeAssistToggle))
        ];

        private IReadOnlyList<StrategyEvaluationResult> EvaluateEnabledStrategySlots(WatchStockItem? stock)
        {
            StrategyEvaluationContext context = new()
            {
                Stock = stock,
                Metrics = _currentStatusMetrics,
                ChartCandleCount = _currentChartCandles.Count,
                MinuteData = BuildStrategyMinuteDataStatus(stock),
                MinuteSnapshots = BuildStrategyMinuteSnapshotSet(stock),
                Market = _isNxtMarketMode ? "NXT" : "KRX",
                IsOwned = IsStockOwned(stock)
            };

            return _strategySlotRegistry.EvaluateEnabled(GetStrategySlotSettings(), context);
        }

        private async Task<int> LoadStrategyMinuteDataAsync(WatchStockItem stock)
        {
            int totalLoaded = 0;
            bool useNxtMarket = ShouldUseNxtDataForStock(stock.Code);
            string market = useNxtMarket ? "NXT" : "KRX";

            foreach ((ChartPeriod Period, int Minute) item in new[]
            {
                (ChartPeriod.Minute1, 1),
                (ChartPeriod.Minute3, 3),
                (ChartPeriod.Minute5, 5),
                (ChartPeriod.Minute10, 10),
                (ChartPeriod.Minute15, 15),
                (ChartPeriod.Minute30, 30)
            })
            {
                ChartCacheKey key = CreateChartCacheKey(stock.Code, useNxtMarket, item.Period);
                int targetCount = CalculateStrategyMinuteTargetCount(stock, item.Minute);
                int cachedCount = _chartMemoryCache.TryGetValue(key, out ChartCacheEntry? entry)
                    ? entry.Candles.Count
                    : 0;

                if (cachedCount >= targetCount)
                {
                    if (entry != null)
                    {
                        _strategyMinuteCacheService.Seed(
                            stock.Code,
                            market,
                            item.Minute,
                            ConvertChartCandlesToDailyCandles(entry.Candles),
                            targetCount);
                    }

                    AppendLog($"strategy minute data cache hit: {stock.Code} / {item.Minute}m / {cachedCount:N0}bars");
                    totalLoaded += cachedCount;
                    continue;
                }

                StrategyMinuteDataLoadStatusText.Text = $"{stock.Name} {item.Minute}분 로드 중... ({cachedCount:N0}/{targetCount:N0})";
                IReadOnlyList<DailyCandle> candles = await _kiwoomConditionService
                    .GetMinuteCandlesAsync(stock.Code, item.Minute, useNxtMarket, targetCount)
                    .ConfigureAwait(true);

                List<ChartCandle> chartCandles = [.. candles.Select(ToChartCandle)];
                SetChartMemoryCache(key, chartCandles, targetCount);
                _strategyMinuteCacheService.Seed(stock.Code, market, item.Minute, candles, targetCount);
                totalLoaded += chartCandles.Count;
                AppendLog($"strategy minute data cache fill: {stock.Code} / {item.Minute}m / {chartCandles.Count:N0}/{targetCount:N0}bars");
            }

            return totalLoaded;
        }

        private static int CalculateStrategyMinuteTargetCount(WatchStockItem stock, int minute)
        {
            int safeMinute = Math.Max(1, minute);
            if (safeMinute == 1)
                return 300;

            const int daysToCover = 7;
            const int sorSessionMinutesPerDay = 660;
            const int warmupBars = 80;
            int barsForBaseCandleRange = (int)Math.Ceiling(daysToCover * sorSessionMinutesPerDay / (double)safeMinute) + warmupBars;

            return Math.Max(MinuteChartCandleCount, barsForBaseCandleRange);
        }

        private StrategyMinuteDataStatus BuildStrategyMinuteDataStatus(WatchStockItem? stock)
        {
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
                return new StrategyMinuteDataStatus();

            bool useNxtMarket = ShouldUseNxtDataForStock(stock.Code);
            string market = useNxtMarket ? "NXT" : "KRX";
            StrategyMinuteDataStatus status = _strategyMinuteCacheService.BuildStatus(stock.Code, market);

            EnsureMinuteDataTarget(status, stock, 1);
            EnsureMinuteDataTarget(status, stock, 3);
            EnsureMinuteDataTarget(status, stock, 5);
            EnsureMinuteDataTarget(status, stock, 10);
            EnsureMinuteDataTarget(status, stock, 15);
            EnsureMinuteDataTarget(status, stock, 30);
            return status;
        }

        private StrategyMinuteSnapshotSet? BuildStrategyMinuteSnapshotSet(WatchStockItem? stock)
        {
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
                return null;

            bool useNxtMarket = ShouldUseNxtDataForStock(stock.Code);
            string market = useNxtMarket ? "NXT" : "KRX";
            return _strategyMinuteCacheService.GetSnapshotSet(stock.Code, market, 1, 3, 5, 10, 15, 30);
        }

        private static List<DailyCandle> ConvertChartCandlesToDailyCandles(IEnumerable<ChartCandle> candles)
        {
            return [.. (candles ?? [])
                .Where(c => c != null && c.Close > 0 && !string.IsNullOrWhiteSpace(c.Date))
                .Select(c => new DailyCandle
                {
                    Date = c.Date,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume,
                    TradingValue = c.Volume > 0 && c.Close > 0
                        ? (long)Math.Min(long.MaxValue, c.Close * c.Volume)
                        : 0
                })];
        }

        private void EnsureMinuteDataTarget(
            StrategyMinuteDataStatus status,
            WatchStockItem stock,
            int minute)
        {
            int targetCount = CalculateStrategyMinuteTargetCount(stock, minute);
            status.SetCount(minute, status.GetCount(minute), targetCount);
        }

        private StrategyDuplicatePolicy GetStrategyDuplicatePolicy() =>
            new(
                AllowAdditionalBuy: IsStrategyToggleOn(DuplicateBuyPolicyToggle),
                NotifyDuplicateSignal: IsStrategyToggleOn(DuplicateAlertPolicyToggle));

        private StrategyExecutionSettings GetStrategyExecutionSettings() =>
            new(
                AutoTradingEnabled: IsStrategyToggleOn(AutoTradingEnabledToggle),
                LiveBuyEnabled: IsStrategyToggleOn(LiveBuyEnabledToggle),
                Budget: ParseLongInput(AutoTradeBudgetTextBox?.Text),
                SlotCount: Math.Max(0, (int)ParseLongInput(AutoTradeSlotCountTextBox?.Text)));

        private void UpdateStrategySlotSummary()
        {
            if (StrategySlotSummaryText == null || StrategySlotStateText == null)
                return;

            IReadOnlyList<StrategySlotSetting> settings = GetStrategySlotSettings();
            int enabledCount = settings.Count(x => x.IsEnabled);

            StrategySlotSummaryText.Text =
                $"Enabled {enabledCount}/{settings.Count} · disabled slots are skipped before evaluation";
            StrategySlotStateText.Text = enabledCount > 0 ? "READY" : "OFF";
            StrategySlotStateText.Foreground = enabledCount > 0
                ? (Brush)FindResource("PaletteLightGreen")
                : (Brush)FindResource("TextMutedBrush");
        }

        private void UpdateStrategyControlBoard()
        {
            if (StrategyControlBoardText == null)
                return;

            StrategyExecutionSettings execution = GetStrategyExecutionSettings();
            StrategyDuplicatePolicy duplicate = GetStrategyDuplicatePolicy();
            bool preloadMinutes = IsStrategyMinutePreloadEnabled();
            bool saveMinuteSeeds = IsStrategyMinuteSeedFileSaveEnabled();
            IReadOnlyList<StrategySlotSetting> settings = GetStrategySlotSettings();
            string enabledStrategies = string.Join(", ", settings
                .Where(x => x.IsEnabled)
                .Select(x => x.Name));

            if (string.IsNullOrWhiteSpace(enabledStrategies))
                enabledStrategies = "none";

            StrategyControlBoardText.Text =
                $"MODE {execution.ExecutionModeText} · " +
                $"ENGINE {(execution.AutoTradingEnabled ? "ON" : "OFF")} · " +
                $"LIVE ORDERS {(execution.LiveBuyEnabled ? "ON" : "OFF(alert only)")} · " +
                $"BUDGET {execution.Budget:N0} · SLOTS {execution.SlotCount} · " +
                $"STRATEGIES {settings.Count(x => x.IsEnabled)}/{settings.Count}: {enabledStrategies} · " +
                $"DUP BUY {(duplicate.AllowAdditionalBuy ? "ON" : "OFF")} / " +
                $"DUP ALERT {(duplicate.NotifyDuplicateSignal ? "ON" : "OFF")} · " +
                $"MINUTE PRELOAD {(preloadMinutes ? "ON" : "OFF")} / " +
                $"FILE SAVE {(saveMinuteSeeds ? "ON" : "OFF")}";
        }

        private void UpdateStrategyProgressRows()
        {
            if (StrategyProgressItemsControl == null)
                return;

            _strategyProgressRows.Clear();

            WatchStockItem? stock = ResolveSelectedProgressStock();
            if (stock == null)
            {
                _strategyProgressRows.Add(StrategyProgressRow.Placeholder(
                    "No selected stock",
                    "종목을 선택하면 전략 진행 상태가 여기에 표시됨",
                    (Brush)FindResource("TextMutedBrush")));
                return;
            }

            if (IsStockOwned(stock) && !GetStrategyDuplicatePolicy().AllowAdditionalBuy)
            {
                _strategyProgressRows.Add(StrategyProgressRow.Placeholder(
                    $"{stock.Name} · owned",
                    "보유 종목 · 중복매수 OFF라서 자동매수 편입 대상에서 제외",
                    (Brush)FindResource("TextMutedBrush")));
                return;
            }

            TryStartStrategyMinutePreloadForSelectedStock();

            IReadOnlyList<StrategyEvaluationResult> results = EvaluateEnabledStrategySlots(stock);
            foreach (StrategyEvaluationResult result in results.Where(ShouldShowStrategyProgressResult))
            {
                StrategyProgressSnapshot progress = result.Progress ?? StrategyProgressSnapshot.Empty(result.SlotId);
                StrategySlotDescriptor? descriptor = _strategySlotRegistry.GetDescriptor(result.SlotId);
                Brush accentBrush = ResolveStrategyProgressBrush(descriptor?.MarketScope, progress.ProgressPercent);

                _strategyProgressRows.Add(new StrategyProgressRow(
                    $"{stock.Name} · {result.Name}",
                    result.StateText,
                    FormatStrategyProgressSummary(result, progress),
                    Math.Clamp(progress.ProgressPercent, 0, 100),
                    $"{Math.Clamp(progress.ProgressPercent, 0, 100):0}%",
                    accentBrush));
            }

            if (_strategyProgressRows.Count == 0)
            {
                _strategyProgressRows.Add(StrategyProgressRow.Placeholder(
                    $"{stock.Name} · filtered",
                    "필터에 맞는 전략 진행 항목이 없음",
                    (Brush)FindResource("TextMutedBrush")));
            }
        }

        private void TryStartStrategyMinutePreloadForSelectedStock()
        {
            if (!IsStrategyMinutePreloadEnabled())
                return;

            WatchStockItem? stock = ResolveSelectedProgressStock();
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
                return;

            string market = ShouldUseNxtDataForStock(stock.Code) ? "NXT" : "KRX";
            string key = $"{NormalizeStockCode(stock.Code)}|{market}";
            if (_strategyMinutePreloadCompletedKeys.Contains(key) ||
                _strategyMinutePreloadRunningKeys.Contains(key))
                return;

            if (IsStrategyMinuteDataReady(stock))
            {
                _strategyMinutePreloadCompletedKeys.Add(key);
                AppendLog($"strategy minute preload READY TO USE: {stock.Code} / {market} / {FormatStrategyMinuteReadiness(stock)}");
                return;
            }

            _ = PreloadStrategyMinuteDataForStockAsync(stock, key);
        }

        private async Task PreloadStrategyMinuteDataForStockAsync(WatchStockItem stock, string key)
        {
            _strategyMinutePreloadRunningKeys.Add(key);
            StrategyMinuteDataLoadStatusText.Text = $"{stock.Name} 전략 분봉 프리로드 시작";
            AppendLog($"strategy minute preload started: {stock.Code} / {key}");

            try
            {
                int loadedCount = await LoadStrategyMinuteDataAsync(stock);
                _strategyMinutePreloadCompletedKeys.Add(key);
                string readiness = FormatStrategyMinuteReadiness(stock);
                StrategyMinuteDataLoadStatusText.Text = $"{stock.Name} 전략 분봉 프리로드 완료: {loadedCount:N0}봉";
                AppendLog($"strategy minute preload READY TO USE: {stock.Code} / {loadedCount:N0}bars / {readiness}");
                UpdateStrategyProgressRows();
            }
            catch (OperationCanceledException)
            {
                StrategyMinuteDataLoadStatusText.Text = $"{stock.Name} 전략 분봉 프리로드 취소";
            }
            catch (Exception ex)
            {
                StrategyMinuteDataLoadStatusText.Text = $"전략 분봉 프리로드 실패: {ex.Message}";
                AppendLog($"strategy minute preload error: {ex.Message}");
            }
            finally
            {
                _strategyMinutePreloadRunningKeys.Remove(key);
            }
        }

        private bool IsStrategyMinuteDataReady(WatchStockItem stock)
        {
            StrategyMinuteDataStatus status = BuildStrategyMinuteDataStatus(stock);
            return status.HasAll(1, 3, 5, 10, 15, 30);
        }

        private string FormatStrategyMinuteReadiness(WatchStockItem stock)
        {
            StrategyMinuteDataStatus status = BuildStrategyMinuteDataStatus(stock);
            return string.Join(" / ", new[] { 1, 3, 5, 10, 15, 30 }
                .Select(minute =>
                {
                    int count = status.GetCount(minute);
                    int target = status.GetTargetCount(minute);
                    string state = count >= target ? "READY" : "WAIT";
                    return $"{minute}m {state} {count:N0}/{target:N0}";
                }));
        }

        private bool IsStrategyMinutePreloadEnabled() =>
            StrategyMinutePreloadToggle == null || IsStrategyToggleOn(StrategyMinutePreloadToggle);

        private bool IsStrategyMinuteSeedFileSaveEnabled() =>
            StrategyMinuteSeedFileSaveToggle != null && IsStrategyToggleOn(StrategyMinuteSeedFileSaveToggle);

        private WatchStockItem? ResolveSelectedProgressStock()
        {
            if (!string.IsNullOrWhiteSpace(_selectedStockCode) &&
                _watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? stock))
                return stock;

            return RecentWatchListBox?.SelectedItem as WatchStockItem
                ?? WatchListBox?.SelectedItem as WatchStockItem;
        }

        private bool ShouldShowStrategyProgressResult(StrategyEvaluationResult result)
        {
            if (!IsStrategyProgressSlotSelected(result.SlotId))
                return false;

            bool isOwned = IsSelectedProgressStockOwned();
            bool showOwned = IsStrategyToggleOn(ProgressFilterOwnedToggle);
            bool showUnowned = IsStrategyToggleOn(ProgressFilterUnownedToggle);

            return isOwned ? showOwned : showUnowned;
        }

        private bool IsStrategyProgressSlotSelected(StrategySlotId slotId) =>
            slotId switch
            {
                StrategySlotId.BaseCandleChase => IsStrategyToggleOn(ProgressFilterBaseCandleChaseToggle),
                StrategySlotId.ThreeMinutePullback => IsStrategyToggleOn(ProgressFilterPullbackToggle),
                StrategySlotId.SorTenMinuteFiveMinuteBreakout => IsStrategyToggleOn(ProgressFilterMiddleToggle),
                StrategySlotId.ThemeDisclosureAssist => IsStrategyToggleOn(ProgressFilterThemeAssistToggle),
                _ => false
            };

        private bool IsSelectedProgressStockOwned()
        {
            if (string.IsNullOrWhiteSpace(_selectedStockCode))
                return false;

            return IsStockOwned(_selectedStockCode);
        }

        private bool IsStockOwned(WatchStockItem? stock) =>
            stock != null && IsStockOwned(stock.Code);

        private bool IsStockOwned(string stockCode)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
                return false;

            return _balanceHoldings.Any(holding =>
                holding.HoldingQuantity > 0 &&
                string.Equals(
                    NormalizeStockCode(holding.StockCode),
                    NormalizeStockCode(stockCode),
                    StringComparison.Ordinal));
        }

        private static string FormatStrategyProgressSummary(StrategyEvaluationResult result, StrategyProgressSnapshot progress)
        {
            string summary = string.IsNullOrWhiteSpace(result.Summary) ? progress.StateText : result.Summary;
            if (progress.TotalSteps <= 0)
                return summary;

            return $"{summary} · step {progress.CurrentStep}/{progress.TotalSteps}";
        }

        private Brush ResolveStrategyProgressBrush(StrategyMarketScope? marketScope, double progressPercent)
        {
            if (progressPercent >= 70)
                return (Brush)FindResource("PaletteLightGreen");

            return marketScope switch
            {
                StrategyMarketScope.KrxOnly => (Brush)FindResource("PalettePink"),
                StrategyMarketScope.NxtOnly => (Brush)FindResource("PaletteSkyBlue"),
                StrategyMarketScope.Sor => (Brush)FindResource("PaletteSkyBlue"),
                StrategyMarketScope.Assist => (Brush)FindResource("PaletteLightYellow"),
                _ => (Brush)FindResource("TextMutedBrush")
            };
        }

        private static bool IsStrategyToggleOn(ToggleButton toggle) =>
            toggle?.IsChecked == true;

        private static long ParseLongInput(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string normalized = text.Replace(",", string.Empty).Trim();
            return long.TryParse(normalized, out long value) ? value : 0;
        }

        private sealed record StrategyProgressRow(
            string Title,
            string StateText,
            string Summary,
            double ProgressPercent,
            string ProgressText,
            Brush AccentBrush)
        {
            public static StrategyProgressRow Placeholder(string title, string summary, Brush brush) =>
                new(title, "WAIT", summary, 0, "0%", brush);
        }
    }
}
