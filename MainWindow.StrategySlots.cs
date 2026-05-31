using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TradingDashboard.Models;
using TradingDashboard.Services;
using TradingDashboard.Services.Strategies;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private readonly StrategySlotRegistry _strategySlotRegistry = StrategySlotRegistry.CreateDefault();
        private readonly System.Collections.ObjectModel.ObservableCollection<StrategyProgressRow> _strategyProgressRows = [];
        private const int StrategyMinuteRequiredCandleCount = 120;
        private bool _isRevertingLockedStrategyToggle;
        private bool _isInitializingStrategyControls = true;

        private void InitializeStrategySlots()
        {
            _isInitializingStrategyControls = true;
            StrategyProgressItemsControl.ItemsSource = _strategyProgressRows;
            if (StrategyMinutePreloadIdleSecondsTextBox != null)
                StrategyMinutePreloadIdleSecondsTextBox.Text = ResolveConfiguredStrategyMinuteAutoPreloadIdleSeconds().ToString();
            _isInitializingStrategyControls = false;

            UpdateStrategyMinutePreloadControlLock();
            UpdateStrategySlotSummary();
            UpdateStrategyControlBoard();
            UpdateStrategyProgressRows();
        }

        private void StrategySlotToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (TryRejectEngineLockedStrategyChange(sender))
                return;

            LogStrategyToggleState(sender);
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
            LogStrategyToggleState(sender);
            UpdateStrategyControlBoard();
            UpdateStrategyMinutePreloadControlLock();
            if (ShouldStartRequiredStrategyMinutePreload(sender))
                StartStrategyMinuteAutoPreload(_watchStocks, force: true, immediate: true);
            if (ShouldStartSelectedStrategyMinutePreload(sender))
                TryStartStrategyMinutePreloadForSelectedStock();
            if (ShouldRescheduleStrategyMinuteAutoPreload(sender))
                StartStrategyMinuteAutoPreload(_watchStocks);
        }

        private void StrategyControlBoardInput_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateStrategyControlBoard();
        }

        private void StrategyMinutePreloadIdleSecondsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStrategyControlBoard();

            if (_isInitializingStrategyControls ||
                StrategyMinutePreloadIdleSecondsTextBox == null ||
                _strategyMinuteAutoPreloadStarted)
                return;

            if (int.TryParse(StrategyMinutePreloadIdleSecondsTextBox.Text, out int seconds))
            {
                int clampedSeconds = Math.Clamp(seconds, 5, 3600);
                _config.StrategyMinutePreload ??= new StrategyMinutePreloadSettings();
                _config.StrategyMinutePreload.IdleDelaySeconds = clampedSeconds;
                try
                {
                    LocalSettingsLoader.SaveStrategyMinutePreloadIdleDelaySeconds(clampedSeconds);
                    AppendLog($"strategy minute preload idle seconds saved: {clampedSeconds}s");
                }
                catch (Exception ex)
                {
                    AppendLog($"strategy minute preload idle seconds save error: {ex.Message}");
                }

                StartStrategyMinuteAutoPreload(_watchStocks);
            }
        }

        private void StrategyProgressFilter_Changed(object sender, RoutedEventArgs e)
        {
            LogStrategyToggleState(sender);
            UpdateStrategyProgressRows();
        }

        private bool ShouldRescheduleStrategyMinuteAutoPreload(object sender)
        {
            return ReferenceEquals(sender, StrategyMinutePreloadToggle);
        }

        private bool ShouldStartSelectedStrategyMinutePreload(object sender)
        {
            return ReferenceEquals(sender, StrategyMinutePreloadToggle);
        }

        private bool ShouldStartRequiredStrategyMinutePreload(object sender)
        {
            return ReferenceEquals(sender, AutoTradingEnabledToggle) &&
                IsStrategyToggleOn(AutoTradingEnabledToggle);
        }

        private void SyncPaperTradingPreviewState()
        {
            if (LiveBuyEnabledToggle == null || VirtualTradingPreviewToggle == null)
                return;

            bool liveOrdersEnabled = IsStrategyToggleOn(LiveBuyEnabledToggle);
            if (liveOrdersEnabled && VirtualTradingPreviewToggle.IsChecked == true)
                VirtualTradingPreviewToggle.IsChecked = false;

            VirtualTradingPreviewToggle.IsEnabled = !liveOrdersEnabled;
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

            AppendLog($"strategy switch blocked while engine is running: {GetStrategyToggleLogName(toggle)} remains {(IsStrategyToggleOn(toggle) ? "ON" : "OFF")}");
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

        private void LogStrategyToggleState(object sender)
        {
            if (sender is not ToggleButton toggle ||
                _isRevertingLockedStrategyToggle)
                return;

            AppendLog($"strategy switch: {GetStrategyToggleLogName(toggle)} {(IsStrategyToggleOn(toggle) ? "ON" : "OFF")}");
        }

        private string GetStrategyToggleLogName(ToggleButton toggle)
        {
            if (ReferenceEquals(toggle, AutoTradingEnabledToggle))
                return "Engine Start";
            if (ReferenceEquals(toggle, LiveBuyEnabledToggle))
                return "Live Orders";
            if (ReferenceEquals(toggle, VirtualTradingPreviewToggle))
                return "Paper Trading";
            if (ReferenceEquals(toggle, StrategySlotBaseCandleChaseToggle))
                return "Slot 1 SOR 10m+3m";
            if (ReferenceEquals(toggle, StrategySlotPullbackToggle))
                return "Slot 2 SOR 15m+5m";
            if (ReferenceEquals(toggle, StrategySlotMiddleToggle))
                return "Slot 3 SOR 10m+5m";
            if (ReferenceEquals(toggle, StrategySlotThemeAssistToggle))
                return "Assist Theme";
            if (ReferenceEquals(toggle, DuplicateBuyPolicyToggle))
                return "Duplicate Buy";
            if (ReferenceEquals(toggle, DuplicateAlertPolicyToggle))
                return "Duplicate Alert";
            if (ReferenceEquals(toggle, StrategyMinutePreloadToggle))
                return "Minute Preload";
            if (ReferenceEquals(toggle, StrategyMinuteSeedFileSaveToggle))
                return "Minute File Save";
            if (ReferenceEquals(toggle, ProgressFilterBaseCandleChaseToggle))
                return "Progress Filter 10m+3m";
            if (ReferenceEquals(toggle, ProgressFilterPullbackToggle))
                return "Progress Filter 15m+5m";
            if (ReferenceEquals(toggle, ProgressFilterMiddleToggle))
                return "Progress Filter 10m+5m";
            if (ReferenceEquals(toggle, ProgressFilterThemeAssistToggle))
                return "Progress Filter Theme";
            if (ReferenceEquals(toggle, ProgressFilterUnownedToggle))
                return "Progress Filter Unowned";
            if (ReferenceEquals(toggle, ProgressFilterOwnedToggle))
                return "Progress Filter Owned";

            return string.IsNullOrWhiteSpace(toggle.Name) ? "unknown toggle" : toggle.Name;
        }

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
                IsOwned = IsStockOwned(stock) && !GetStrategyDuplicatePolicy().AllowAdditionalBuy
            };

            return _strategySlotRegistry.EvaluateEnabled(GetStrategySlotSettings(), context);
        }

        private async Task<int> LoadStrategyMinuteDataAsync(WatchStockItem stock)
        {
            int totalLoaded = 0;
            bool useNxtMarket = ShouldUseNxtDataForStock(stock.Code);
            string market = useNxtMarket ? "NXT" : "KRX";
            bool saveSeedFiles = IsStrategyMinuteSeedFileSaveEnabled();

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
                if (saveSeedFiles &&
                    _strategyMinuteSeedFileStore.TryLoadToday(stock.Code, market, item.Minute, targetCount, out List<DailyCandle> seedFileCandles))
                {
                    _strategyMinuteCacheService.Seed(stock.Code, market, item.Minute, seedFileCandles, targetCount);
                    SetChartMemoryCache(key, [.. seedFileCandles.Select(ToChartCandle)], targetCount);
                    AppendLog(seedFileCandles.Count >= targetCount
                        ? $"strategy minute seed file hit: {stock.Code} / {market} / {item.Minute}m / {seedFileCandles.Count:N0}bars"
                        : $"strategy minute seed file refill: {stock.Code} / {market} / {item.Minute}m / {seedFileCandles.Count:N0}->{targetCount:N0}bars");

                    if (seedFileCandles.Count >= targetCount)
                    {
                        totalLoaded += seedFileCandles.Count;
                        continue;
                    }
                }

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
                        if (saveSeedFiles)
                        {
                            List<DailyCandle> memoryCandles = ConvertChartCandlesToDailyCandles(entry.Candles);
                            _strategyMinuteSeedFileStore.SaveToday(stock.Code, market, item.Minute, memoryCandles, targetCount);
                            AppendLog($"strategy minute seed file saved: {stock.Code} / {market} / {item.Minute}m / {Math.Min(memoryCandles.Count, targetCount):N0}bars");
                        }
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
                if (saveSeedFiles)
                {
                    _strategyMinuteSeedFileStore.SaveToday(stock.Code, market, item.Minute, candles, targetCount);
                    AppendLog($"strategy minute seed file saved: {stock.Code} / {market} / {item.Minute}m / {Math.Min(candles.Count, targetCount):N0}bars");
                }

                totalLoaded += chartCandles.Count;
                AppendLog($"strategy minute data cache fill: {stock.Code} / {item.Minute}m / {chartCandles.Count:N0}/{targetCount:N0}bars");
            }

            SaveStrategyAnchorForStock(stock, market);
            return totalLoaded;
        }

        private void SaveStrategyAnchorForStock(WatchStockItem stock, string market)
        {
            if (stock == null ||
                string.IsNullOrWhiteSpace(stock.Code) ||
                string.IsNullOrWhiteSpace(stock.GateBaseCandleDate) ||
                !TryParseYyyyMMdd(stock.GateBaseCandleDate, out DateTime baseDate))
                return;

            var touches = new Dictionary<int, StrategyMa60TouchAnchor>();
            foreach (int minute in new[] { 5, 10, 15, 30 })
            {
                if (_strategyMinuteCacheService.TryGetLastMa60TouchAnchor(stock.Code, market, minute, baseDate, out StrategyMa60TouchAnchor touch))
                {
                    touches[minute] = touch;
                    AppendLog($"strategy anchor ma60 touch: {stock.Code} / {market} / {minute}m / {touch.Time:HH:mm} / ma60 {touch.Ma60:0.##}");
                }
                else
                {
                    AppendLog($"strategy anchor ma60 touch missing: {stock.Code} / {market} / {minute}m / {stock.GateBaseCandleDate}");
                }
            }

            if (touches.Count == 0)
                return;

            WatchlistStockCacheEntry? cache = GetWatchlistMemoryCache(stock.Code);
            var document = new StrategyAnchorDocument
            {
                Code = NormalizeStockCode(stock.Code),
                Market = string.Equals(market, "NXT", StringComparison.OrdinalIgnoreCase) ? "NXT" : "KRX",
                BaseDate = baseDate.ToString("yyyyMMdd"),
                GateBaseCandleFound = stock.GateBaseCandleFound,
                GateBaseCandleOffset = stock.GateBaseCandleOffset,
                GateBaseCandleMarket = stock.GateBaseCandleMarket,
                GateBaseCandleChangeRate = stock.GateBaseCandleChangeRate,
                GateBaseCandleTradeValue = stock.GateBaseCandleTradeValue,
                BasePrice = cache?.BasePrice ?? 0,
                BasePriceDate = cache?.BasePriceDate ?? string.Empty,
                BasePriceSource = cache?.BasePriceSource ?? string.Empty,
                SavedAt = DateTime.Now.ToString("yyyyMMddHHmmss"),
                Ma60Touches = touches
            };

            _strategyAnchorStore.Save(document);
            AppendReadyLog($"strategy anchor saved: {stock.Code} / {market} / base {document.BaseDate} / {touches.Count}/4 ma60 touches");
        }

        private static bool TryParseYyyyMMdd(string text, out DateTime date)
        {
            string digits = new([.. (text ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 8)
                digits = digits[..8];

            return DateTime.TryParseExact(
                digits,
                "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date);
        }

        private static int CalculateStrategyMinuteTargetCount(WatchStockItem stock, int minute)
        {
            return StrategyMinuteRequiredCandleCount;
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
            bool paperTradingPreview = IsPaperTradingPreviewEnabled();
            bool preloadMinutes = IsStrategyMinutePreloadEnabled();
            bool saveMinuteSeeds = IsStrategyMinuteSeedFileSaveEnabled();
            int preloadIdleSeconds = ResolveStrategyMinuteAutoPreloadIdleSeconds();
            IReadOnlyList<StrategySlotSetting> settings = GetStrategySlotSettings();
            StrategyWatchReadiness readiness = BuildStrategyWatchReadiness();
            string runState = ResolveStrategyRunStateText(execution, preloadMinutes, settings, readiness);
            string enabledStrategies = string.Join(", ", settings
                .Where(x => x.IsEnabled)
                .Select(x => x.Name));

            if (string.IsNullOrWhiteSpace(enabledStrategies))
                enabledStrategies = "none";

            UpdateStrategyRunLamp(runState);

            StrategyControlBoardText.Text =
                $"RUN STATE {runState} · " +
                $"MINUTE READY {readiness.ReadyCount:N0}/{readiness.TotalCount:N0} · " +
                $"{ResolveStrategyRunDetailText(runState, readiness)}\n" +
                $"MODE {execution.ExecutionModeText} · " +
                $"ENGINE {(execution.AutoTradingEnabled ? "ON" : "OFF")} · " +
                $"LIVE ORDERS {(execution.LiveBuyEnabled ? "ON" : "OFF(alert only)")} · " +
                $"PAPER {(paperTradingPreview ? "ON" : "OFF")} · " +
                $"BUDGET {execution.Budget:N0} · SLOTS {execution.SlotCount} · " +
                $"STRATEGIES {settings.Count(x => x.IsEnabled)}/{settings.Count}: {enabledStrategies} · " +
                $"DUP BUY {(duplicate.AllowAdditionalBuy ? "ON" : "OFF")} / " +
                $"DUP ALERT {(duplicate.NotifyDuplicateSignal ? "ON" : "OFF")} · " +
                $"MINUTE PRELOAD {(preloadMinutes ? "ON" : "OFF")} / " +
                $"IDLE {preloadIdleSeconds}s / FILE SAVE {(saveMinuteSeeds ? "ON" : "OFF")}";
        }

        private StrategyWatchReadiness BuildStrategyWatchReadiness()
        {
            List<WatchStockItem> stocks = [.. (_watchStocks ?? [])
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => NormalizeStockCode(stock.Code), StringComparer.Ordinal)
                .Select(group => group.First())];

            int ready = stocks.Count(IsStrategyMinuteDataReady);
            return new StrategyWatchReadiness(ready, stocks.Count);
        }

        private string ResolveStrategyRunStateText(
            StrategyExecutionSettings execution,
            bool preloadMinutes,
            IReadOnlyList<StrategySlotSetting> settings,
            StrategyWatchReadiness readiness)
        {
            if (settings.Count(x => x.IsEnabled) == 0)
                return "BLOCK";

            if (!execution.AutoTradingEnabled)
                return readiness.IsAllReady ? "READY" : "WATCH OFF";

            if (readiness.TotalCount == 0 || !readiness.IsAllReady)
                return "WAIT DATA";

            return "RUNNING";
        }

        private static string ResolveStrategyRunDetailText(string runState, StrategyWatchReadiness readiness)
        {
            return runState switch
            {
                "READY" => "minute ledger READY; press Engine Start to monitor",
                "RUNNING" => "monitoring active",
                "WAIT DATA" => readiness.TotalCount == 0
                    ? "waiting for watch stocks; monitoring starts after minute ledger READY"
                    : "minute ledger preparing; monitoring starts as stocks become READY",
                "BLOCK" => "strategy setup blocked; enable at least one strategy",
                _ => readiness.TotalCount > 0
                    ? "watch is OFF; data can preload in the background"
                    : "watch is OFF; no watch stocks loaded"
            };
        }

        private void UpdateStrategyRunLamp(string runState)
        {
            if (StrategyRunLampText == null || StrategyRunLampEllipse == null)
                return;

            StrategyRunLampText.Text = runState;
            Brush brush = runState switch
            {
                "READY" => (Brush)FindResource("PaletteLightGreen"),
                "RUNNING" => (Brush)FindResource("PaletteGreen"),
                "WAIT DATA" => (Brush)FindResource("PaletteYellow"),
                "BLOCK" => (Brush)FindResource("PaletteRed"),
                _ => (Brush)FindResource("TextMutedBrush")
            };

            StrategyRunLampText.Foreground = brush;
            StrategyRunLampEllipse.Fill = brush;
        }

        private readonly record struct StrategyWatchReadiness(int ReadyCount, int TotalCount)
        {
            public bool IsAllReady => TotalCount > 0 && ReadyCount >= TotalCount;
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
            ProcessStrategySignalAlerts(stock, results);
            ProcessPaperTradingPreview(stock, results);
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
                AppendReadyLog($"strategy minute preload READY TO USE: {stock.Code} / {market} / {FormatStrategyMinuteReadiness(stock)}");
                return;
            }

            _ = PreloadStrategyMinuteDataForStockAsync(stock, key);
        }

        private async Task PreloadStrategyMinuteDataForStockAsync(WatchStockItem stock, string key)
        {
            _strategyMinutePreloadRunningKeys.Add(key);
            UpdateStrategyMinutePreloadControlLock();
            UpdateStrategyControlBoard();
            StrategyMinuteDataLoadStatusText.Text = $"{stock.Name} 전략 분봉 프리로드 시작";
            AppendLog($"strategy minute preload started: {stock.Code} / {key}");

            try
            {
                int loadedCount = await LoadStrategyMinuteDataAsync(stock);
                _strategyMinutePreloadCompletedKeys.Add(key);
                string readiness = FormatStrategyMinuteReadiness(stock);
                StrategyMinuteDataLoadStatusText.Text = $"{stock.Name} 전략 분봉 프리로드 완료: {loadedCount:N0}봉";
                AppendReadyLog($"strategy minute preload READY TO USE: {stock.Code} / {loadedCount:N0}bars / {readiness}");
                UpdateStrategyControlBoard();
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
                UpdateStrategyMinutePreloadControlLock();
                UpdateStrategyControlBoard();
            }
        }

        private bool IsStrategyMinuteDataReady(WatchStockItem stock)
        {
            StrategyMinuteSnapshotSet? snapshots = BuildStrategyMinuteSnapshotSet(stock);
            if (snapshots == null)
                return false;

            IReadOnlyList<StrategySlotSetting> enabledSettings = [.. GetStrategySlotSettings().Where(x => x.IsEnabled)];
            bool hasMinuteStrategy = false;
            foreach (StrategySlotSetting setting in enabledSettings)
            {
                switch (setting.Id)
                {
                    case StrategySlotId.BaseCandleChase:
                        hasMinuteStrategy = true;
                        if (!snapshots.HasMa60AndBreakout20(10, 3))
                            return false;
                        break;
                    case StrategySlotId.ThreeMinutePullback:
                        hasMinuteStrategy = true;
                        if (!snapshots.HasMa60AndBreakout20(15, 5))
                            return false;
                        break;
                    case StrategySlotId.SorTenMinuteFiveMinuteBreakout:
                        hasMinuteStrategy = true;
                        if (!snapshots.HasMa60AndBreakout20(10, 5))
                            return false;
                        break;
                }
            }

            return hasMinuteStrategy || enabledSettings.Count > 0;
        }

        private string FormatStrategyMinuteReadiness(WatchStockItem stock)
        {
            StrategyMinuteDataStatus status = BuildStrategyMinuteDataStatus(stock);
            StrategyMinuteSnapshotSet? snapshots = BuildStrategyMinuteSnapshotSet(stock);
            List<string> parts = [];

            foreach (StrategySlotSetting setting in GetStrategySlotSettings().Where(x => x.IsEnabled))
            {
                switch (setting.Id)
                {
                    case StrategySlotId.BaseCandleChase:
                        parts.Add(snapshots?.FormatMa60AndBreakout20(10, 3) ?? "10m:MA60 wait / 3m:20H wait");
                        break;
                    case StrategySlotId.ThreeMinutePullback:
                        parts.Add(snapshots?.FormatMa60AndBreakout20(15, 5) ?? "15m:MA60 wait / 5m:20H wait");
                        break;
                    case StrategySlotId.SorTenMinuteFiveMinuteBreakout:
                        parts.Add(snapshots?.FormatMa60AndBreakout20(10, 5) ?? "10m:MA60 wait / 5m:20H wait");
                        break;
                }
            }

            if (parts.Count > 0)
                return string.Join(" / ", parts.Distinct());

            if (GetStrategySlotSettings().Any(x => x.IsEnabled))
                return "minute indicators not required";

            return string.Join(" / ", new[] { 1, 3, 5, 10, 15, 30 }
                .Select(minute => $"{minute}m {status.GetCount(minute):N0}/{status.GetTargetCount(minute):N0}"));
        }

        private bool IsStrategyMinutePreloadEnabled() =>
            StrategyMinutePreloadToggle == null || IsStrategyToggleOn(StrategyMinutePreloadToggle);

        private bool IsStrategyMinuteSeedFileSaveEnabled() =>
            StrategyMinuteSeedFileSaveToggle != null && IsStrategyToggleOn(StrategyMinuteSeedFileSaveToggle);

        private void StartStrategyMinuteAutoPreload(IEnumerable<WatchStockItem> stocks, bool force = false, bool immediate = false)
        {
            bool wasRunning = _strategyMinuteAutoPreloadStarted;
            if (force && wasRunning)
            {
                AppendLog("strategy minute required preload already running");
                UpdateStrategyControlBoard();
                return;
            }

            _strategyMinuteAutoPreloadCts?.Cancel();

            if (!force && !IsStrategyMinutePreloadEnabled())
            {
                AppendLog("strategy minute auto preload skipped: switch OFF");
                UpdateStrategyMinutePreloadControlLock();
                return;
            }

            List<WatchStockItem> snapshot = [.. (stocks ?? [])
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => NormalizeStockCode(stock.Code), StringComparer.Ordinal)
                .Select(group => group.First())];

            if (snapshot.Count == 0)
                return;

            string batchKey = string.Join("|", snapshot
                .Select(stock => NormalizeStockCode(stock.Code))
                .OrderBy(code => code, StringComparer.Ordinal));
            if (string.IsNullOrWhiteSpace(batchKey) ||
                (!force && _strategyMinuteAutoPreloadBatchKeys.Contains(batchKey) && !_strategyMinuteAutoPreloadStarted))
                return;

            _strategyMinuteAutoPreloadCts = new CancellationTokenSource();
            CancellationToken token = _strategyMinuteAutoPreloadCts.Token;
            TimeSpan idleDelay = immediate ? TimeSpan.Zero : ResolveStrategyMinuteAutoPreloadIdleDelay();
            UpdateStrategyMinutePreloadControlLock();
            AppendLog(wasRunning
                ? $"strategy minute auto preload interrupted and rescheduled: {snapshot.Count}stocks / idle {idleDelay.TotalSeconds:0}s"
                : force
                    ? $"strategy minute required preload started by Engine Start: {snapshot.Count}stocks"
                    : $"strategy minute auto preload scheduled: {snapshot.Count}stocks / idle {idleDelay.TotalSeconds:0}s");
            _ = RunStrategyMinuteAutoPreloadAfterIdleAsync(snapshot, batchKey, idleDelay, token, force);
        }

        private TimeSpan ResolveStrategyMinuteAutoPreloadIdleDelay()
        {
            return TimeSpan.FromSeconds(ResolveStrategyMinuteAutoPreloadIdleSeconds());
        }

        private int ResolveStrategyMinuteAutoPreloadIdleSeconds()
        {
            if (StrategyMinutePreloadIdleSecondsTextBox != null &&
                int.TryParse(StrategyMinutePreloadIdleSecondsTextBox.Text, out int secondsFromInput))
                return Math.Clamp(secondsFromInput, 5, 3600);

            return ResolveConfiguredStrategyMinuteAutoPreloadIdleSeconds();
        }

        private int ResolveConfiguredStrategyMinuteAutoPreloadIdleSeconds()
        {
            int seconds = _config.StrategyMinutePreload?.IdleDelaySeconds ?? 180;
            return Math.Clamp(seconds, 5, 3600);
        }

        private void UpdateStrategyMinutePreloadControlLock()
        {
            bool preloadEnabled = IsStrategyMinutePreloadEnabled();
            if (StrategyMinutePreloadIdleSecondsTextBox != null)
                StrategyMinutePreloadIdleSecondsTextBox.IsEnabled = !preloadEnabled;
            if (StrategyMinuteSeedFileSaveToggle != null)
                StrategyMinuteSeedFileSaveToggle.IsEnabled = !preloadEnabled;
        }

        private async Task RunStrategyMinuteAutoPreloadAfterIdleAsync(
            IReadOnlyList<WatchStockItem> stocks,
            string batchKey,
            TimeSpan idleDelay,
            CancellationToken cancellationToken,
            bool force)
        {
            try
            {
                await Task.Delay(idleDelay, cancellationToken);
                _strategyMinuteAutoPreloadBatchKeys.Add(batchKey);
                _strategyMinuteAutoPreloadStarted = true;
                UpdateStrategyMinutePreloadControlLock();
                UpdateStrategyControlBoard();
                await PreloadStrategyMinuteDataForStocksAsync(stocks, cancellationToken, force);
            }
            catch (OperationCanceledException)
            {
                if (_strategyMinuteAutoPreloadStarted)
                {
                    _strategyMinuteAutoPreloadStarted = false;
                    UpdateStrategyMinutePreloadControlLock();
                    UpdateStrategyControlBoard();
                    AppendLog("strategy minute auto preload interrupted while running");
                }
                else
                {
                    AppendLog("strategy minute auto preload rescheduled before idle");
                }
            }
        }

        private async Task PreloadStrategyMinuteDataForStocksAsync(IReadOnlyList<WatchStockItem> stocks, CancellationToken cancellationToken, bool force)
        {
            int completed = 0;
            AppendLog($"strategy minute auto preload continuous run started: {stocks.Count}stocks");

            foreach (WatchStockItem stock in stocks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!force && !IsStrategyMinutePreloadEnabled())
                {
                    AppendLog($"strategy minute auto preload stopped: switch OFF / completed {completed:N0}/{stocks.Count:N0}");
                    _strategyMinuteAutoPreloadStarted = false;
                    UpdateStrategyMinutePreloadControlLock();
                    UpdateStrategyControlBoard();
                    return;
                }

                string market = ShouldUseNxtDataForStock(stock.Code) ? "NXT" : "KRX";
                string key = $"{NormalizeStockCode(stock.Code)}|{market}";
                if (_strategyMinutePreloadCompletedKeys.Contains(key))
                {
                    SaveStrategyMinuteSeedFilesFromMemoryIfEnabled(stock, market);
                    completed++;
                    UpdateStrategyControlBoard();
                    AppendReadyLog($"strategy minute auto preload stock already ready: {completed:N0}/{stocks.Count:N0} / {stock.Code} {stock.Name} / {market} / {FormatStrategyMinuteReadiness(stock)}");
                    continue;
                }

                if (_strategyMinutePreloadRunningKeys.Contains(key))
                {
                    await WaitForStrategyMinutePreloadToFinishAsync(key, cancellationToken);
                    if (IsStrategyMinuteDataReady(stock))
                    {
                        _strategyMinutePreloadCompletedKeys.Add(key);
                        SaveStrategyMinuteSeedFilesFromMemoryIfEnabled(stock, market);
                        completed++;
                        UpdateStrategyControlBoard();
                        AppendReadyLog($"strategy minute auto preload stock done: {completed:N0}/{stocks.Count:N0} / {stock.Code} {stock.Name} / {market} / {FormatStrategyMinuteReadiness(stock)}");
                    }

                    continue;
                }

                if (IsStrategyMinuteDataReady(stock))
                {
                    _strategyMinutePreloadCompletedKeys.Add(key);
                    SaveStrategyMinuteSeedFilesFromMemoryIfEnabled(stock, market);
                    completed++;
                    UpdateStrategyControlBoard();
                    AppendReadyLog($"strategy minute auto preload stock done: {completed:N0}/{stocks.Count:N0} / {stock.Code} {stock.Name} / {market} / {FormatStrategyMinuteReadiness(stock)}");
                    continue;
                }

                await PreloadStrategyMinuteDataForStockAsync(stock, key);
                completed++;
                UpdateStrategyControlBoard();
                AppendReadyLog($"strategy minute auto preload stock done: {completed:N0}/{stocks.Count:N0} / {stock.Code} {stock.Name} / {FormatStrategyMinuteReadiness(stock)}");
            }

            _strategyMinuteAutoPreloadStarted = false;
            UpdateStrategyMinutePreloadControlLock();
            UpdateStrategyControlBoard();
            AppendReadyLog($"strategy minute auto preload ALL READY TO USE: {completed:N0}/{stocks.Count:N0}stocks");
        }

        private async Task WaitForStrategyMinutePreloadToFinishAsync(string key, CancellationToken cancellationToken)
        {
            while (_strategyMinutePreloadRunningKeys.Contains(key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(250, cancellationToken);
            }
        }

        private void SaveStrategyMinuteSeedFilesFromMemoryIfEnabled(WatchStockItem stock, string market)
        {
            if (!IsStrategyMinuteSeedFileSaveEnabled())
                return;

            bool useNxtMarket = string.Equals(market, "NXT", StringComparison.Ordinal);
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
                if (!_chartMemoryCache.TryGetValue(key, out ChartCacheEntry? entry) ||
                    entry.Candles.Count <= 0)
                    continue;

                List<DailyCandle> candles = ConvertChartCandlesToDailyCandles(entry.Candles);
                if (candles.Count <= 0)
                    continue;

                _strategyMinuteSeedFileStore.SaveToday(stock.Code, market, item.Minute, candles, targetCount);
                AppendLog($"strategy minute seed file saved: {stock.Code} / {market} / {item.Minute}m / {Math.Min(candles.Count, targetCount):N0}bars");
            }
        }

        private void ProcessPaperTradingPreview(
            WatchStockItem stock,
            IReadOnlyList<StrategyEvaluationResult> results)
        {
            StrategyExecutionSettings execution = GetStrategyExecutionSettings();
            if (!execution.AutoTradingEnabled ||
                execution.LiveBuyEnabled ||
                !IsPaperTradingPreviewEnabled() ||
                !IsStrategyMinuteDataReady(stock) ||
                ResolveStrategySignalPrice(stock) <= 0)
                return;

            string armedKey = $"{NormalizeStockCode(stock.Code)}|PAPER_ARMED|{DateTime.Today:yyyyMMdd}";
            if (_paperTradingPreviewLoggedKeys.Add(armedKey))
            {
                AppendLog(
                    $"PAPER TEST ARMED: {stock.Code} {stock.Name} / price {stock.LastPrice:N0} / " +
                    $"budget {execution.Budget:N0} / live order BLOCKED");
            }

            foreach (StrategyEvaluationResult result in results.Where(x => x.HasSignal))
            {
                string key = $"{NormalizeStockCode(stock.Code)}|{result.SlotId}|{DateTime.Today:yyyyMMdd}";
                if (!_paperTradingPreviewLoggedKeys.Add(key))
                    continue;

                PaperPositionLedgerEntry? entry = TryRecordPaperBuy(stock, result, execution);
                if (entry == null)
                    continue;

                AppendReadyLog(
                    $"PAPER BUY MARKED: {stock.Code} {stock.Name} / {entry.SlotTag} / {result.Name} / " +
                    $"price {entry.EntryPrice:N0} / qty {entry.Quantity:N0} / virtual only");
            }
        }

        private bool IsPaperTradingPreviewEnabled() =>
            VirtualTradingPreviewToggle != null && IsStrategyToggleOn(VirtualTradingPreviewToggle);

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
