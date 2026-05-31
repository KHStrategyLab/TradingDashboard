using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TradingDashboard.Models;
using TradingDashboard.Services;
using TradingDashboard.Services.Strategies;
using TradingDashboard.Services.Trading;

namespace TradingDashboard
{
    public partial class MainWindow : Window
    {
        private const int MaxLogLines = 500;
        private const int MaxWatchlistMemoryCacheCount = 200;
        private const int DuplicateStockSelectionBlockMs = 200;
        private const string KrxPreviousCloseBasePriceSource = "KRX_PREV_CLOSE";
        private const int GateBaseCandleLookbackCount = 10;
        private const long GateBaseCandleMinTradeValue = 100_000_000_000;
        private const double GateBaseCandleMinChangeRate = 20.0;
        private const double ChartRightPadding = 25d;
        private readonly AppConfig _config;
        private readonly NaverNewsService _newsService;
        private readonly NewsKeywordFilterService _newsKeywordFilterService = new();
        private readonly NewsThumbnailService _newsThumbnailService = new();
        private readonly DartDisclosureService _disclosureService;
        private readonly DartDisclosureAlertService _disclosureAlertService;
        private readonly TelegramNotifier _telegramNotifier;
        private readonly KiwoomRestConditionService _kiwoomConditionService;
        private readonly KiwoomTradingClient _tradingClient;
        private readonly TradingCostCalculator _tradingCostCalculator;
        private readonly StrategyMinuteCacheService _strategyMinuteCacheService = new();
        private readonly StrategyMinuteSeedFileStore _strategyMinuteSeedFileStore = new();
        private readonly StrategyAnchorStore _strategyAnchorStore = new();
        private readonly StrategyOrderJournalStore _strategyOrderJournalStore = new();
        private readonly StrategyPositionLedgerStore _strategyPositionLedgerStore = new();
        private readonly PaperPositionLedgerStore _paperPositionLedgerStore = new();
        private readonly WatchlistStockCacheStore _watchlistCacheStore = new();
        private readonly ChartCandleCacheStore _chartCandleFileCacheStore = new();
        private readonly Queue<LogLineEntry> _logLines = new();
        private readonly Brush _upColorBrush;
        private readonly Brush _downColorBrush;
        private readonly Brush _aggressiveBuyQtyBrush;
        private readonly Brush _rateUpBrush;
        private readonly Brush _rateDownBrush;
        private readonly Brush _whiteBrush;
        private readonly Brush _hogaCurrentPriceBorderBrush;
        private readonly Brush _hogaCurrentPriceBackgroundBrush;
        private readonly ObservableCollection<WatchStockItem> _watchStocks = [];
        private readonly ObservableCollection<WatchStockItem> _recentViewedStocks = [];
        private readonly ObservableCollection<StockMasterItem> _stockSearchSuggestions = [];
        private bool _stockSearchAutocompleteSuppressed;
        private readonly ObservableCollection<TradePrint> _recentTrades = [];
        private readonly ObservableCollection<HogaLevel> _sellHogaLevels = [];
        private readonly ObservableCollection<HogaLevel> _buyHogaLevels = [];
        private readonly ObservableCollection<KiwoomHolding> _balanceHoldings = [];
        private readonly ObservableCollection<PaperPositionLedgerEntry> _paperPositions = [];
        private readonly List<NewsItem> _marketNewsCache = [];
        private readonly Dictionary<string, WatchStockItem> _watchStockByCode = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WatchlistStockCacheEntry> _watchlistMemoryCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClosingSnapshotCacheEntry> _closingSnapshotMemoryCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastTickPriceByCode = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastStrategyMinuteCumulativeVolumeByKey = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategyMinutePreloadCompletedKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategyMinutePreloadRunningKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategyMinuteAutoPreloadBatchKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _paperTradingPreviewLoggedKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategySignalAlertLoggedKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategyLiveBuyOrderKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategyExitAlertLoggedKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _strategyLiveSellOrderKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _strategyLiveOrderRetryAfterByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _strategyLiveOrderBlockedLogAfterByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StrategyPositionLedgerEntry> _strategyPositionLedgerByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ManualBuyStopAnchor> _manualBuyStopAnchorsByCode = new(StringComparer.Ordinal);
        private readonly HashSet<string> _manualBuyStopAnchorLoadingCodes = new(StringComparer.Ordinal);
        private readonly object _strategyLiveOrderLock = new();
        private readonly Dictionary<string, long> _lastBuyExecCumByCode = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastSellExecCumByCode = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _conditionRealtimeEnterSemaphore = new(1, 1);
        private readonly HashSet<string> _conditionRealtimeEnterPendingCodes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _conditionRealtimeActiveCodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _conditionRealtimeGenerationByCode = new(StringComparer.Ordinal);
        private readonly object _conditionRealtimeEnterPendingLock = new();
        private long _conditionRealtimeGenerationSequence;
        private readonly HashSet<string> _lateNewsSentStockCodes = new(StringComparer.Ordinal);
        private readonly object _lateNewsLock = new();
        private readonly DateTime _lateNewsAppStartedAt = DateTime.Now;
        private readonly HashSet<string> _conditionEnterAlertSentStockCodes = new(StringComparer.Ordinal);
        private readonly object _conditionEnterAlertLock = new();
        private DateTime _conditionEnterAlertSentDate = DateTime.Today;
        private const int MinuteChartCandleCount = 700;
        private const int DailyChartRealtimeDrawIntervalMs = 350;
        private const int MinuteChartRealtimeDrawIntervalMs = 1500;
        private const int MaxChartMemoryCacheEntries = 200;
        private const int MaxChartMemoryCacheCandles = 100_000;
        private const int MinChartDragCandleCount = 10;
        private readonly List<ChartCandle> _currentChartCandles = [];
        private readonly Dictionary<ChartCacheKey, ChartCacheEntry> _chartMemoryCache = [];
        private long _chartCacheAccessSequence;
        private int _chartRenderVersion;
        private bool _initialChartFileCachePreloadStarted;
        private bool _strategyMinuteAutoPreloadStarted;
        private string _lastAcceptedWatchSelectionKey = string.Empty;
        private DateTime _lastAcceptedWatchSelectionAt = DateTime.MinValue;
        private CancellationTokenSource? _stockSearchSuggestionCts;
        private ChartPeriod _currentChartPeriod = ChartPeriod.Daily;
        private ChartPeriod _currentChartDataPeriod = ChartPeriod.Daily;
        private string _currentChartCode = string.Empty;
        private DateTime _lastRealtimeChartDrawAt = DateTime.MinValue;
        private ChartRenderState? _priceChartRenderState;
        private ChartRenderState? _volumeChartRenderState;
        private Line? _lastCandleWick;
        private Rectangle? _lastCandleBody;
        private Rectangle? _lastVolumeBar;
        private Line? _currentPriceMarkerLine;
        private Border? _currentPriceMarkerLabel;
        private TextBlock? _currentPriceMarkerText;
        private Canvas? _priceChartCanvas;
        private Rectangle? _chartDragSelectionRect;
        private bool _isChartDragSelecting;
        private Point _chartDragStartPoint;
        private int _chartViewStartIndex;
        private int _chartViewCount;
        private string _selectedStockCode = string.Empty;
        private long _buyTradeVolume;
        private long _sellTradeVolume;
        private StockStatusMetrics _currentStatusMetrics = new();
        private long _krxPrevClosePrice;
        private long _selectedPreviousVolume;
        private bool _selectedUsesUnifiedDailyVolume;
        private int _selectionVersion;
        private DateTime _last0DReceivedAt = DateTime.MinValue;
        private string _lastMarketStatusCode = string.Empty;
        private string _lastMarketStatusTime = string.Empty;
        private string _lastMarketExpectedRemain = string.Empty;
        private string _lastMarketStatusText = "Market status unknown";
        private string _conditionRealtimeSeq = string.Empty;
        private DateTime _lastMarketStatusAt = DateTime.MinValue;
        private DateTime _marketStatusUnknownUntil = DateTime.MinValue;
        private bool _isNxtMarketMode;
        private bool _isMarketNewsLoading;
        private DateTime _marketNewsCacheLoadedAt = DateTime.MinValue;
        private DateTime _lateNewsSentDate = DateTime.Today;
        private ClientWebSocket? _realtimeWs;
        private CancellationTokenSource? _realtimeCts;
        private CancellationTokenSource? _selectedRequestCts;
        private CancellationTokenSource? _chartRequestCts;
        private CancellationTokenSource? _watchlistCacheRefreshCts;
        private CancellationTokenSource? _strategyMinuteAutoPreloadCts;
        private static readonly string[] MarketStatusTypes = ["0s"];
        private static readonly string[] SellPriceKeys = ["41", "42", "43", "44", "45", "46", "47", "48", "49", "50"];
        private static readonly string[] SellQtyKeys = ["61", "62", "63", "64", "65", "66", "67", "68", "69", "70"];
        private static readonly string[] BuyPriceKeys = ["51", "52", "53", "54", "55", "56", "57", "58", "59", "60"];
        private static readonly string[] BuyQtyKeys = ["71", "72", "73", "74", "75", "76", "77", "78", "79", "80"];

        private sealed class ClosingSnapshotCacheEntry
        {
            public KrxClosingSnapshot Snapshot { get; set; } = new KrxClosingSnapshot();
            public bool IsNxtSnapshot { get; set; }
            public DateTime CachedAt { get; set; } = DateTime.Now;
        }

        public MainWindow()
        {
            _config = LocalSettingsLoader.Load();

            InitializeComponent();
            InitializeStrategySlots();

            _upColorBrush = (Brush)FindResource("PaletteRed");
            _downColorBrush = (Brush)FindResource("PaletteBlue");
            _aggressiveBuyQtyBrush = (Brush)FindResource("PalettePink");
            _rateUpBrush = (Brush)FindResource("PalettePink");
            _rateDownBrush = (Brush)FindResource("PaletteSkyBlue");
            _whiteBrush = (Brush)FindResource("PaletteWhite");
            _hogaCurrentPriceBorderBrush = (Brush)FindResource("PaletteSkyBlue");
            _hogaCurrentPriceBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x32, 0x3E));

            _newsService = new NaverNewsService(_config.NaverNews);
            _newsService.ApiLimitLog += message => Dispatcher.Invoke(() => AppendLog(message));
            _disclosureService = new DartDisclosureService(_config.Dart);
            _disclosureService.ApiLimitLog += message => Dispatcher.Invoke(() => AppendLog(message));
            _telegramNotifier = new TelegramNotifier(_config.Telegram);
            _telegramNotifier.ApiLimitLog += message => Dispatcher.Invoke(() => AppendLog(message));
            _disclosureAlertService = new DartDisclosureAlertService(_disclosureService, _telegramNotifier);
            _disclosureAlertService.AlertLog += message => Dispatcher.Invoke(() => AppendLog(message));
            _kiwoomConditionService = new KiwoomRestConditionService(_config.Kiwoom);
            _kiwoomConditionService.RestLimitLog += message => Dispatcher.Invoke(() => AppendLog(message));
            _tradingClient = new KiwoomTradingClient(_config.Kiwoom, _kiwoomConditionService.GetAccessTokenAsync);
            _tradingClient.ApiLimitLog += message => Dispatcher.Invoke(() => AppendLog(message));
            _tradingCostCalculator = new TradingCostCalculator(_config.TradingCosts);
            LoadStrategyOrderJournal();
            LoadStrategyPositionLedger();
            LoadPaperPositionLedger();
            LoadWatchlistCache();
            WatchListBox.ItemsSource = _watchStocks;
            RecentWatchListBox.ItemsSource = _recentViewedStocks;
            StockSearchSuggestionListBox.ItemsSource = _stockSearchSuggestions;
            RecentTradeListBox.ItemsSource = _recentTrades;
            BalanceHoldingsDataGrid.ItemsSource = _balanceHoldings;
            BalanceHoldingsScrollableDataGrid.ItemsSource = _balanceHoldings;
            PaperPositionsDataGrid.ItemsSource = _paperPositions;
            SellQtyListBox.ItemsSource = _sellHogaLevels;
            SellPriceListBox.ItemsSource = _sellHogaLevels;
            BuyPriceListBox.ItemsSource = _buyHogaLevels;
            BuyQtyListBox.ItemsSource = _buyHogaLevels;
            for (int i = 0; i < 10; i++)
            {
                _sellHogaLevels.Add(new HogaLevel());
                _buyHogaLevels.Add(new HogaLevel());
            }
            HogaStatusText.Text = "Price - / Rate - / 0D -";
            UpdateHogaSummary(0, 0);

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppendLog("App started");
            try
            {
                SetStartupLoading(
                    true,
                    "Starting TradingDashboard...",
                    $"Watchlist cache ready: {_watchlistMemoryCache.Count} items",
                    "Preparing Kiwoom/DART/Naver workers");

                SetStartupLoading(
                    true,
                    "Checking market status...",
                    "Opening Kiwoom 0s status channel",
                    "Choosing KRX/NXT mode before loading watchlist");
                await PrimeMarketStatusBeforeWatchlistAsync();

                string conditionLabel = GetConfiguredConditionLabel();
                SetStartupLoading(
                    true,
                    $"Loading {conditionLabel} watchlist...",
                    "Requesting Kiwoom condition search results",
                    "Realtime registration will start after the list is ready");
                await LoadWatchListFromKiwoomConditionAsync();
                SetStartupLoading(
                    true,
                    "Starting realtime feeds...",
                    $"{_watchStocks.Count} watchlist stocks ready",
                    "News and filings will continue in the background");
                _ = LoadMarketNewsAfterStartupDelayAsync();
                _ = RefreshBalanceAsync("startup");
            }
            finally
            {
                SetStartupLoading(false, string.Empty, string.Empty, string.Empty);
            }

            if (WatchListBox.SelectedItem is not ListBoxItem)
            {
                WatchListBox.SelectedIndex = 0;
            }
        }

        private async Task LoadMarketNewsAfterStartupDelayAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            await LoadMarketNewsAsync();
        }

        private async Task PrimeMarketStatusBeforeWatchlistAsync()
        {
            if (!_config.Kiwoom.UseRestApi)
                return;

            try
            {
                AppendLog("0s market status prime started");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                CancellationToken ct = cts.Token;
                string token = await _kiwoomConditionService.GetAccessTokenAsync(ct);

                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri("wss://api.kiwoom.com:10000/api/dostk/websocket"), ct);
                await SendWsJsonAsync(ws, new { trnm = "LOGIN", token }, ct);
                using JsonDocument login = await ReceiveByTrNameAsync(ws, "LOGIN", ct);
                string loginCode = ReadString(login.RootElement, "return_code");
                if (loginCode != "0")
                {
                    AppendLog($"0s prime LOGIN failed: {loginCode}");
                    return;
                }

                await RegisterMarketStatusAsync(ws, ct);
                MarketStatusSnapshot status = await ReceiveMarketStatusSnapshotAsync(ws, ct);
                if (string.IsNullOrWhiteSpace(status.Code))
                {
                    MarkMarketStatusUnknown();
                    AppendLog($"0s market status prime returned no value, Market status unknown({(IsNxtMarketWindow() ? "time-window NXT" : "KRX first")})");
                    return;
                }

                ApplyMarketStatusSnapshot(status, allowRefresh: false);
                AppendLog($"0s market status prime: 215={_lastMarketStatusCode} / {_lastMarketStatusText} / {(_isNxtMarketMode ? "use NXT" : "use KRX")}");
            }
            catch (OperationCanceledException)
            {
                MarkMarketStatusUnknown();
                AppendLog($"0s market status prime timed out, Market status unknown({(IsNxtMarketWindow() ? "time-window NXT" : "KRX first")})");
            }
            catch (Exception ex)
            {
                MarkMarketStatusUnknown();
                AppendLog($"0s market status prime error: {ex.Message}");
            }
        }

        private void MarkMarketStatusUnknown()
        {
            _lastMarketStatusCode = string.Empty;
            _lastMarketStatusTime = string.Empty;
            _lastMarketExpectedRemain = string.Empty;
            _lastMarketStatusText = "Market status unknown";
            _lastMarketStatusAt = DateTime.MinValue;
            _marketStatusUnknownUntil = DateTime.Now.AddMinutes(2);
            _isNxtMarketMode = IsNxtMarketWindow();
        }

        private async Task LoadWatchListFromKiwoomConditionAsync()
        {
            string conditionLabel = GetConfiguredConditionLabel();
            try
            {
                if (!_config.Kiwoom.UseRestApi)
                {
                    SetStartupLoading(
                        true,
                        "Loading cached watchlist...",
                        "Kiwoom REST is disabled",
                        "Using local data only");
                    AppendLog("Kiwoom REST disabled(UseRestApi=false)");
                    _krxPrevClosePrice = 0;
                    return;
                }

                SetStartupLoading(
                    true,
                    $"Loading {conditionLabel} watchlist...",
                    "Kiwoom condition request in progress",
                    "Waiting for candidate stocks");
                AppendLog($"Kiwoom {conditionLabel} query started");

                List<WatchStockItem> stocks = await _kiwoomConditionService.GetConditionStocksAsync();
                if (stocks.Count == 0)
                {
                    SetStartupLoading(
                        true,
                        "Condition result is empty",
                        "Trying local watchlist cache",
                        "Realtime will start if cached stocks exist");
                    AppendLog("condition result 0 items");
                    ApplyCachedWatchListFallback("condition result 0 items");
                    return;
                }

                SetStartupLoading(
                    true,
                    "Checking base-candle gate...",
                    $"{stocks.Count} stocks received from {conditionLabel}",
                    "Discarding stocks without a recent 100B/20% candle");
                List<WatchStockItem> gatedStocks = await FilterWatchlistByBaseCandleGateAsync(stocks);

                SetStartupLoading(
                    true,
                    "Applying gated watchlist...",
                    $"{gatedStocks.Count} of {stocks.Count} stocks passed",
                    "Saving cache and preparing realtime registration");
                ApplyWatchList(gatedStocks);
                if (gatedStocks.Count > 0)
                    SaveDailyWatchlistSnapshot(gatedStocks);
                else
                    AppendLog("watchlist cache save skipped: gate pass 0items");
                AppendLog($"condition result {stocks.Count}items / gate pass {gatedStocks.Count}items applied");
                ScheduleWatchlistBasePriceRefresh(gatedStocks, TimeSpan.FromSeconds(30));
                StartInitialChartFileCachePreload(gatedStocks);
                StartStrategyMinuteAutoPreload(gatedStocks);
                _ = StartRealtimeTradeAsync();
            }
            catch (Exception ex)
            {
                SetStartupLoading(
                    true,
                    "Condition request failed",
                    "Trying local watchlist cache",
                    ex.Message);
                AppendLog($"condition query error: {ex.Message}");
                if (!ApplyCachedWatchListFallback("condition query failed"))
                    MessageBox.Show($"Kiwoom {conditionLabel} query error: {ex.Message}");
            }
        }

        private string GetConfiguredConditionLabel()
        {
            string seq = string.IsNullOrWhiteSpace(_config.Kiwoom.ConditionSeq01)
                ? "1"
                : _config.Kiwoom.ConditionSeq01.Trim();

            return $"condition {seq}";
        }

        private async Task<List<WatchStockItem>> FilterWatchlistByBaseCandleGateAsync(IEnumerable<WatchStockItem> stocks, CancellationToken cancellationToken = default)
        {
            var result = new List<WatchStockItem>();
            foreach (WatchStockItem stock in stocks.Where(s => !string.IsNullOrWhiteSpace(s.Code)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await TryApplyBaseCandleGateAsync(stock, cancellationToken))
                {
                    result.Add(stock);
                    continue;
                }

                AppendLog($"gate discard: {stock.Name} ({stock.Code}) / no 10-day 100B+20% candle");
            }

            return result;
        }

        private async Task<bool> TryApplyBaseCandleGateAsync(WatchStockItem stock, CancellationToken cancellationToken = default)
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            WatchlistStockCacheEntry? cache = GetWatchlistMemoryCache(stock.Code);
            if (cache != null && IsActiveGateBaseCandleCache(cache, today))
            {
                ApplyGateCacheToStock(stock, cache);
                return true;
            }

            BaseCandleGateResult? gate = await FindBaseCandleGateAsync(stock.Code, useNxtMarket: false, market: "KRX", cancellationToken);
            if (gate == null && stock.SupportsNxt && (ShouldUseNxtMarketNow() || IsNxtFrozenWindow()))
                gate = await FindBaseCandleGateAsync(stock.Code, useNxtMarket: true, market: "NXT", cancellationToken);

            ApplyGateResultToStock(stock, gate);
            if (gate == null)
                return false;

            WatchlistStockCacheEntry entry = UpsertWatchlistMemoryCache(
                stock.Code,
                string.IsNullOrWhiteSpace(stock.Name) ? stock.Code : stock.Name,
                stock.SupportsNxt,
                stock.MarketTypeCode,
                stock.MarketName,
                stock.ProgramMarketType,
                stock.CurrentPrice,
                stock.ChangeAmount,
                stock.ChangeRateText,
                stock.VolumeText,
                stock.LastPrice,
                stock.OrderWarning,
                stock.AuditInfo,
                stock.StockState,
                stock.SectorName,
                saveFile: false);
            ApplyGateStockToCache(stock, entry, today);
            return stock.GateBaseCandleFound;
        }

        private async Task<BaseCandleGateResult?> FindBaseCandleGateAsync(string code, bool useNxtMarket, string market, CancellationToken cancellationToken)
        {
            List<DailyCandle> candles = await _kiwoomConditionService
                .GetDailyCandlesAsync(code, useNxtMarket, GateBaseCandleLookbackCount + 1, cancellationToken)
                .ConfigureAwait(false);
            if (candles.Count < 2)
                return null;

            List<DailyCandle> ordered = [.. candles.OrderBy(c => c.Date)];
            int start = Math.Max(1, ordered.Count - GateBaseCandleLookbackCount);
            for (int i = ordered.Count - 1; i >= start; i--)
            {
                DailyCandle candle = ordered[i];
                DailyCandle prev = ordered[i - 1];
                if (prev.Close <= 0)
                    continue;

                double changeRate = (candle.Close - prev.Close) / prev.Close * 100.0;
                long estimatedTradingValue = EstimateTradingValue(candle);
                long tradingValue = Math.Max(candle.TradingValue, estimatedTradingValue);
                if (tradingValue < GateBaseCandleMinTradeValue || changeRate < GateBaseCandleMinChangeRate)
                    continue;

                int offset = ordered.Count - 1 - i;
                return new BaseCandleGateResult(
                    offset,
                    candle.Date,
                    market,
                    changeRate,
                    tradingValue);
            }

            return null;
        }

        private static long EstimateTradingValue(DailyCandle candle)
        {
            if (candle.Close <= 0 || candle.Volume <= 0)
                return 0;

            return (long)Math.Min(long.MaxValue, candle.Close * (double)candle.Volume);
        }

        private static void ApplyGateResultToStock(WatchStockItem stock, BaseCandleGateResult? gate)
        {
            stock.GateBaseCandleFound = gate != null;
            stock.GateBaseCandleOffset = gate?.Offset ?? -1;
            stock.GateBaseCandleDate = gate?.Date ?? string.Empty;
            stock.GateBaseCandleMarket = gate?.Market ?? string.Empty;
            stock.GateBaseCandleChangeRate = gate?.ChangeRate ?? 0;
            stock.GateBaseCandleTradeValue = gate?.TradeValue ?? 0;
        }

        private static void ApplyGateCacheToStock(WatchStockItem stock, WatchlistStockCacheEntry entry)
        {
            stock.GateBaseCandleFound = entry.GateBaseCandleFound;
            if (entry.GateBaseCandleOffset >= 0)
                stock.GateBaseCandleOffset = entry.GateBaseCandleOffset;
            stock.GateBaseCandleDate = entry.GateBaseCandleDate;
            stock.GateBaseCandleMarket = entry.GateBaseCandleMarket;
            stock.GateBaseCandleChangeRate = entry.GateBaseCandleChangeRate;
            stock.GateBaseCandleTradeValue = entry.GateBaseCandleTradeValue;
        }

        private static void ApplyGateStockToCache(WatchStockItem stock, WatchlistStockCacheEntry entry, string checkedDate)
        {
            entry.GateBaseCandleFound = stock.GateBaseCandleFound;
            entry.GateBaseCandleOffset = stock.GateBaseCandleOffset;
            entry.GateBaseCandleDate = stock.GateBaseCandleDate;
            entry.GateBaseCandleMarket = stock.GateBaseCandleMarket;
            entry.GateBaseCandleChangeRate = stock.GateBaseCandleChangeRate;
            entry.GateBaseCandleTradeValue = stock.GateBaseCandleTradeValue;
            entry.GateBaseCandleCheckedDate = checkedDate;
        }

        private static bool IsActiveGateBaseCandleCache(WatchlistStockCacheEntry? entry, string today)
        {
            return entry is { GateBaseCandleFound: true } &&
                string.Equals(entry.GateBaseCandleCheckedDate, today, StringComparison.Ordinal);
        }

        private bool ApplyCachedWatchListFallback(string reason)
        {
            List<WatchStockItem> cachedStocks = BuildWatchListFromCache();
            if (cachedStocks.Count == 0)
            {
                SetStartupLoading(
                    true,
                    "No cached watchlist available",
                    reason,
                    "Waiting for a usable condition result");
                AppendLog($"{reason}, no usable watchlist cache");
                return false;
            }

            SetStartupLoading(
                true,
                "Applying cached watchlist...",
                $"{cachedStocks.Count} cached stocks ready",
                "Realtime registration will use cached candidates");
            ApplyWatchList(cachedStocks);
            AppendLog($"{reason}, watchlist cache {cachedStocks.Count}items applied");
            ScheduleWatchlistBasePriceRefresh(cachedStocks, TimeSpan.FromSeconds(30));
            StartInitialChartFileCachePreload(cachedStocks);
            StartStrategyMinuteAutoPreload(cachedStocks);
            _ = StartRealtimeTradeAsync();
            return true;
        }

        private List<WatchStockItem> BuildWatchListFromCache()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            List<WatchlistStockCacheEntry> entries = [.. _watchlistMemoryCache.Values
                .Where(e => !string.IsNullOrWhiteSpace(e.Code) &&
                    IsActiveGateBaseCandleCache(e, today) &&
                    (e.SnapshotDate == today || e.LastSeenConditionDate == today))
                .OrderBy(e => e.LastUsedAt)];

            if (entries.Count == 0)
            {
                entries = [.. _watchlistMemoryCache.Values
                    .Where(e => !string.IsNullOrWhiteSpace(e.Code) &&
                        IsActiveGateBaseCandleCache(e, today))
                    .OrderBy(e => e.LastUsedAt)
                    .Take(10)];
            }

            return [.. entries
                .Select(e => new WatchStockItem
                {
                    Code = e.Code,
                    Name = string.IsNullOrWhiteSpace(e.Name) ? e.Code : e.Name,
                    MarketTypeCode = e.MarketTypeCode,
                    MarketName = e.MarketName,
                    ProgramMarketType = e.ProgramMarketType,
                    CurrentPrice = e.CurrentPrice,
                    ChangeAmount = e.ChangeAmount,
                    ChangeRateText = e.ChangeRateText,
                    VolumeText = e.VolumeText,
                    LastPrice = e.LastPrice,
                    OrderWarning = e.OrderWarning,
                    AuditInfo = e.AuditInfo,
                    StockState = e.StockState,
                    SectorName = e.SectorName,
                    GateBaseCandleFound = e.GateBaseCandleFound,
                    GateBaseCandleOffset = e.GateBaseCandleOffset,
                    GateBaseCandleDate = e.GateBaseCandleDate,
                    GateBaseCandleMarket = e.GateBaseCandleMarket,
                    GateBaseCandleChangeRate = e.GateBaseCandleChangeRate,
                    GateBaseCandleTradeValue = e.GateBaseCandleTradeValue,
                    SupportsNxt = e.SupportsNxt
                })];
        }

        private void ApplyWatchList(IEnumerable<WatchStockItem> stocks)
        {
            var cleanStocks = stocks
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => stock.Code ?? string.Empty, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            _watchStocks.Clear();
            _watchStockByCode.Clear();
            if (cleanStocks.Count == 0)
            {
                AppendLog("left list refreshed: 0items");
                return;
            }

            foreach (WatchStockItem stock in cleanStocks)
            {
                if (string.IsNullOrWhiteSpace(stock.Name))
                    stock.Name = stock.Code;
                if (string.IsNullOrWhiteSpace(stock.ChangeRateText))
                    stock.ChangeRateText = "-";
                if (string.IsNullOrWhiteSpace(stock.VolumeText))
                    stock.VolumeText = "-";
                ApplyWatchlistTradeValueEstimate(stock);
                stock.PriceBrush = stock.ChangeAmount > 0 ? _upColorBrush : stock.ChangeAmount < 0 ? _downColorBrush : _whiteBrush;
                ApplyWatchlistCacheToStock(stock);
                ApplyWatchlistTradeValueEstimate(stock);
                _watchStocks.Add(stock);
                if (!string.IsNullOrWhiteSpace(stock.Code))
                    _watchStockByCode[stock.Code] = stock;
            }

            AppendLog("left list refreshed");
        }

        private void LoadWatchlistCache()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                foreach (WatchlistStockCacheEntry entry in _watchlistCacheStore.Load())
                {
                    if (string.IsNullOrWhiteSpace(entry.Code))
                        continue;

                    entry.LastUsedAt = DateTime.Now;
                    if (entry.SnapshotDate != today)
                        entry.LastSeenConditionDate = string.Empty;
                    _watchlistMemoryCache[entry.Code] = entry;
                }

                TrimWatchlistMemoryCache();
                AppendLog($"watchlist cache loaded: {_watchlistMemoryCache.Count}items");
            }
            catch (Exception ex)
            {
                AppendLog($"watchlist cache loaded error: {ex.Message}");
            }
        }

        private void SaveDailyWatchlistSnapshot(IEnumerable<WatchStockItem> stocks)
        {
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                var snapshot = new List<WatchlistStockCacheEntry>();
                foreach (WatchStockItem stock in stocks.Where(s => !string.IsNullOrWhiteSpace(s.Code)))
                {
                    WatchlistStockCacheEntry entry = UpsertWatchlistMemoryCache(
                        stock.Code,
                        string.IsNullOrWhiteSpace(stock.Name) ? stock.Code : stock.Name,
                        stock.SupportsNxt,
                        stock.MarketTypeCode,
                        stock.MarketName,
                        stock.ProgramMarketType,
                        stock.CurrentPrice,
                        stock.ChangeAmount,
                        stock.ChangeRateText,
                        stock.VolumeText,
                        stock.LastPrice,
                        stock.OrderWarning,
                        stock.AuditInfo,
                        stock.StockState,
                        stock.SectorName,
                        saveFile: false);
                    if (!IsTrustedKrxBasePrice(entry, today))
                        ClearCachedBasePrice(entry);

                    ApplyGateStockToCache(stock, entry, string.IsNullOrWhiteSpace(entry.GateBaseCandleCheckedDate) ? today : entry.GateBaseCandleCheckedDate);
                    entry.SnapshotDate = today;
                    entry.LastSeenConditionDate = today;
                    entry.Market = entry.SupportsNxt ? "KRX,NXT" : "KRX";
                    snapshot.Add(entry);
                }

                _watchlistCacheStore.Save(snapshot);
                AppendLog($"watchlist cache saved: {snapshot.Count}items");
            }
            catch (Exception ex)
            {
                AppendLog($"watchlist cache save error: {ex.Message}");
            }
        }

        private void ScheduleWatchlistBasePriceRefresh(IEnumerable<WatchStockItem> stocks, TimeSpan idleDelay)
        {
            _watchlistCacheRefreshCts?.Cancel();
            _watchlistCacheRefreshCts = new CancellationTokenSource();
            CancellationToken token = _watchlistCacheRefreshCts.Token;
            List<WatchStockItem> snapshot = [.. stocks
                .Where(s => !string.IsNullOrWhiteSpace(s.Code))
                .Select(s => new WatchStockItem
                {
                    Code = s.Code,
                    Name = s.Name,
                    MarketTypeCode = s.MarketTypeCode,
                    MarketName = s.MarketName,
                    ProgramMarketType = s.ProgramMarketType,
                    CurrentPrice = s.CurrentPrice,
                    ChangeAmount = s.ChangeAmount,
                    ChangeRateText = s.ChangeRateText,
                    VolumeText = s.VolumeText,
                    LastPrice = s.LastPrice,
                    OrderWarning = s.OrderWarning,
                    AuditInfo = s.AuditInfo,
                    StockState = s.StockState,
                    SectorName = s.SectorName,
                    SupportsNxt = s.SupportsNxt
                })];

            _ = RefreshWatchlistBasePricesInBackgroundAsync(snapshot, idleDelay, token);
        }

        private async Task RefreshWatchlistBasePricesInBackgroundAsync(IEnumerable<WatchStockItem> stocks, TimeSpan idleDelay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(idleDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string today = DateTime.Now.ToString("yyyyMMdd");
            var snapshotCodes = stocks
                .Where(s => !string.IsNullOrWhiteSpace(s.Code))
                .Select(s => s.Code)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            bool changed = false;
            foreach (string code in snapshotCodes)
            {
                if (!_watchlistMemoryCache.TryGetValue(code, out WatchlistStockCacheEntry? entry))
                    continue;
                if (IsTrustedKrxBasePrice(entry, today))
                    continue;

                try
                {
                    long basePrice = await _kiwoomConditionService.GetKrxPreviousClosePriceAsync(code, cancellationToken);
                    if (basePrice <= 0)
                        continue;

                    entry.BasePrice = basePrice;
                    entry.BasePriceDate = today;
                    entry.BasePriceSource = KrxPreviousCloseBasePriceSource;
                    entry.LastUsedAt = DateTime.Now;
                    changed = true;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Background cache hydration should not disturb the screen.
                }
            }

            if (!changed)
                return;

            Dispatcher.Invoke(() => AppendLog("watchlist base-price memory refreshed"));
        }

        private void ApplyWatchlistCacheToStock(WatchStockItem stock)
        {
            WatchlistStockCacheEntry? entry = GetWatchlistMemoryCache(stock.Code);
            if (entry == null)
                return;

            if (string.IsNullOrWhiteSpace(stock.Name))
                stock.Name = entry.Name;
            if (string.IsNullOrWhiteSpace(stock.MarketTypeCode))
                stock.MarketTypeCode = entry.MarketTypeCode;
            if (string.IsNullOrWhiteSpace(stock.MarketName))
                stock.MarketName = entry.MarketName;
            if (string.IsNullOrWhiteSpace(stock.ProgramMarketType))
                stock.ProgramMarketType = entry.ProgramMarketType;
            if (stock.CurrentPrice <= 0)
                stock.CurrentPrice = entry.CurrentPrice;
            if (stock.ChangeAmount == 0)
                stock.ChangeAmount = entry.ChangeAmount;
            if (string.IsNullOrWhiteSpace(stock.ChangeRateText) || stock.ChangeRateText == "-")
                stock.ChangeRateText = string.IsNullOrWhiteSpace(entry.ChangeRateText) ? stock.ChangeRateText : entry.ChangeRateText;
            if (string.IsNullOrWhiteSpace(stock.VolumeText) || stock.VolumeText == "-")
                stock.VolumeText = string.IsNullOrWhiteSpace(entry.VolumeText) ? stock.VolumeText : entry.VolumeText;
            ApplyWatchlistTradeValueEstimate(stock);
            if (stock.LastPrice <= 0)
                stock.LastPrice = entry.LastPrice;
            if (string.IsNullOrWhiteSpace(stock.OrderWarning))
                stock.OrderWarning = entry.OrderWarning;
            if (string.IsNullOrWhiteSpace(stock.AuditInfo))
                stock.AuditInfo = entry.AuditInfo;
            if (string.IsNullOrWhiteSpace(stock.StockState))
                stock.StockState = entry.StockState;
            if (string.IsNullOrWhiteSpace(stock.SectorName))
                stock.SectorName = entry.SectorName;
            if (IsActiveGateBaseCandleCache(entry, DateTime.Now.ToString("yyyyMMdd")))
            {
                ApplyGateCacheToStock(stock, entry);
            }
        }

        private WatchlistStockCacheEntry? GetWatchlistMemoryCache(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || !_watchlistMemoryCache.TryGetValue(code, out WatchlistStockCacheEntry? entry))
                return null;

            entry.LastUsedAt = DateTime.Now;
            return entry;
        }

        private static void ApplyWatchlistTradeValueEstimate(WatchStockItem stock)
        {
            if (stock.TodayTradeValue > 0)
                return;

            long volume = ParseLongAbs(stock.VolumeText);
            if (stock.CurrentPrice <= 0 || volume <= 0)
                return;

            stock.TodayTradeValue = (long)Math.Min(long.MaxValue, stock.CurrentPrice * (double)volume);
        }

        private static bool IsTrustedKrxBasePrice(WatchlistStockCacheEntry? entry, string date)
        {
            // base price invariant: screen base price must use only KRX previous close.
            // NXT close/current/LastPrice or untrusted cache values must not overwrite base price.
            return entry != null &&
                entry.BasePrice > 0 &&
                string.Equals(entry.BasePriceDate, date, StringComparison.Ordinal) &&
                string.Equals(entry.BasePriceSource, KrxPreviousCloseBasePriceSource, StringComparison.Ordinal);
        }

        private static void SetCachedKrxBasePrice(WatchlistStockCacheEntry entry, long basePrice, string date)
        {
            // Only KRX previous close is locked as base price here.
            entry.BasePrice = basePrice;
            entry.BasePriceDate = date;
            entry.BasePriceSource = KrxPreviousCloseBasePriceSource;
        }

        private static void ClearCachedBasePrice(WatchlistStockCacheEntry entry)
        {
            entry.BasePrice = 0;
            entry.BasePriceDate = string.Empty;
            entry.BasePriceSource = string.Empty;
        }

        private WatchlistStockCacheEntry UpsertWatchlistMemoryCache(string code, string name, bool? supportsNxt, string? marketTypeCode, string? marketName, string? programMarketType, long? currentPrice, long? changeAmount, string? changeRateText, string? volumeText, long? lastPrice, string? orderWarning, string? auditInfo, string? stockState, string? sectorName, bool saveFile)
        {
            if (!_watchlistMemoryCache.TryGetValue(code, out WatchlistStockCacheEntry? entry))
            {
                entry = new WatchlistStockCacheEntry { Code = code };
                _watchlistMemoryCache[code] = entry;
            }

            entry.Name = string.IsNullOrWhiteSpace(name) ? entry.Name : name.Trim();
            if (supportsNxt.HasValue)
                entry.SupportsNxt = supportsNxt.Value;
            entry.Market = entry.SupportsNxt ? "KRX,NXT" : "KRX";
            if (!string.IsNullOrWhiteSpace(marketTypeCode))
                entry.MarketTypeCode = marketTypeCode.Trim();
            if (!string.IsNullOrWhiteSpace(marketName))
                entry.MarketName = marketName.Trim();
            if (!string.IsNullOrWhiteSpace(programMarketType))
                entry.ProgramMarketType = programMarketType.Trim();
            if (currentPrice.HasValue && currentPrice.Value > 0)
                entry.CurrentPrice = currentPrice.Value;
            if (changeAmount.HasValue)
                entry.ChangeAmount = changeAmount.Value;
            if (!string.IsNullOrWhiteSpace(changeRateText) && changeRateText != "-")
                entry.ChangeRateText = changeRateText.Trim();
            if (!string.IsNullOrWhiteSpace(volumeText) && volumeText != "-")
                entry.VolumeText = volumeText.Trim();
            if (lastPrice.HasValue && lastPrice.Value > 0)
                entry.LastPrice = lastPrice.Value;
            if (!string.IsNullOrWhiteSpace(orderWarning))
                entry.OrderWarning = orderWarning.Trim();
            if (!string.IsNullOrWhiteSpace(auditInfo))
                entry.AuditInfo = auditInfo.Trim();
            if (!string.IsNullOrWhiteSpace(stockState))
                entry.StockState = stockState.Trim();
            if (!string.IsNullOrWhiteSpace(sectorName))
                entry.SectorName = sectorName.Trim();
            entry.LastUsedAt = DateTime.Now;

            TrimWatchlistMemoryCache();
            if (saveFile)
                _watchlistCacheStore.Save(_watchlistMemoryCache.Values);
            return entry;
        }

        private void TrimWatchlistMemoryCache()
        {
            if (_watchlistMemoryCache.Count <= MaxWatchlistMemoryCacheCount)
                return;

            foreach (string code in _watchlistMemoryCache
                         .OrderBy(kv => kv.Value.LastUsedAt)
                         .Take(_watchlistMemoryCache.Count - MaxWatchlistMemoryCacheCount)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                _watchlistMemoryCache.Remove(code);
            }
        }

        private async void WatchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox || listBox.SelectedItem is null)
                return;

            await LoadNewsForSelectedStockAsync(listBox.SelectedItem);
        }

        private async void StockSearchButton_Click(object sender, RoutedEventArgs e)
        {
            await SearchAndOpenStockAsync();
        }

        private async void StockSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CancelStockSearchSuggestion();
            if (_stockSearchAutocompleteSuppressed)
            {
                HideStockSearchSuggestions();
                return;
            }

            string query = StockSearchTextBox.Text.Trim();
            if (query.Length < 2)
            {
                HideStockSearchSuggestions();
                return;
            }

            _stockSearchSuggestionCts = new CancellationTokenSource();
            CancellationToken token = _stockSearchSuggestionCts.Token;

            try
            {
                await Task.Delay(700, token);
                IReadOnlyList<StockMasterItem> suggestions = await _kiwoomConditionService
                    .SearchStockMasterItemsAsync(query, 20, token);

                if (token.IsCancellationRequested)
                    return;

                _stockSearchSuggestions.Clear();
                foreach (StockMasterItem item in suggestions)
                    _stockSearchSuggestions.Add(item);

                StockSearchSuggestionListBox.Visibility = _stockSearchSuggestions.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                if (_stockSearchSuggestions.Count > 0)
                {
                    StockSearchSuggestionListBox.SelectedIndex = 0;
                    FocusFirstStockSearchSuggestion();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                HideStockSearchSuggestions();
                AppendLog($"stock autocomplete error: {ex.GetType().Name} / {ex.Message}");
            }
        }

        private async void StockSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (StockSearchSuggestionListBox.Visibility == Visibility.Visible &&
                (e.Key == Key.Down || e.Key == Key.Up))
            {
                MoveStockSearchSuggestionSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && StockSearchSuggestionListBox.Visibility == Visibility.Visible)
            {
                HideStockSearchSuggestions();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            if (StockSearchSuggestionListBox.Visibility == Visibility.Visible &&
                StockSearchSuggestionListBox.SelectedItem is StockMasterItem)
            {
                await OpenSelectedStockSuggestionAsync();
                return;
            }

            await SearchAndOpenStockAsync();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key != Key.S || (Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
                return;

            _stockSearchAutocompleteSuppressed = false;
            StockSearchTextBox.Focus();
            StockSearchTextBox.SelectAll();
            e.Handled = true;
        }

        private void StockSearchTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _stockSearchAutocompleteSuppressed = false;
        }

        private void StockSearchTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _stockSearchAutocompleteSuppressed = false;
        }

        private void MoveStockSearchSuggestionSelection(int delta)
        {
            int count = StockSearchSuggestionListBox.Items.Count;
            if (count <= 0)
                return;

            int current = StockSearchSuggestionListBox.SelectedIndex;
            if (current < 0)
                current = delta > 0 ? -1 : 0;

            int next = Math.Clamp(current + delta, 0, count - 1);
            StockSearchSuggestionListBox.SelectedIndex = next;
            StockSearchSuggestionListBox.ScrollIntoView(StockSearchSuggestionListBox.SelectedItem);
        }

        private void FocusFirstStockSearchSuggestion()
        {
            StockSearchSuggestionListBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (StockSearchSuggestionListBox.Visibility != Visibility.Visible ||
                    StockSearchSuggestionListBox.Items.Count <= 0)
                    return;

                StockSearchSuggestionListBox.UpdateLayout();
                StockSearchSuggestionListBox.SelectedIndex = StockSearchSuggestionListBox.SelectedIndex < 0
                    ? 0
                    : StockSearchSuggestionListBox.SelectedIndex;
                StockSearchSuggestionListBox.ScrollIntoView(StockSearchSuggestionListBox.SelectedItem);
                StockSearchSuggestionListBox.UpdateLayout();

                if (StockSearchSuggestionListBox.ItemContainerGenerator.ContainerFromIndex(StockSearchSuggestionListBox.SelectedIndex) is ListBoxItem item)
                {
                    item.Focus();
                    return;
                }

                StockSearchSuggestionListBox.Focus();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private async void StockSearchSuggestionListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideStockSearchSuggestions();
                StockSearchTextBox.Focus();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await OpenSelectedStockSuggestionAsync();
        }

        private async void StockSearchSuggestionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await OpenSelectedStockSuggestionAsync();
        }

        private async Task OpenSelectedStockSuggestionAsync()
        {
            if (StockSearchSuggestionListBox.SelectedItem is not StockMasterItem selected)
                return;

            _stockSearchAutocompleteSuppressed = true;
            HideStockSearchSuggestions();
            StockSearchTextBox.Text = selected.Name;
            await SearchAndOpenStockAsync(selected.Code);
            FocusSelectedRecentStock();
        }

        private async Task SearchAndOpenStockAsync(string? forcedQuery = null)
        {
            string query = (forcedQuery ?? StockSearchTextBox.Text).Trim();
            if (string.IsNullOrWhiteSpace(query))
                return;

            StockSearchButton.IsEnabled = false;
            try
            {
                WatchStockItem? existing = _watchStocks
                    .Concat(_recentViewedStocks)
                    .FirstOrDefault(stock =>
                        string.Equals(stock.Code, query, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(stock.Name, query, StringComparison.OrdinalIgnoreCase));

                WatchStockItem? stock = existing;
                if (stock is null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    stock = await _kiwoomConditionService.SearchListedStockAsync(query, cts.Token);
                }

                if (stock is null)
                {
                    AppendLog($"stock search no result: {query}");
                    return;
                }

                AddRecentViewedStock(stock);
                if (ReferenceEquals(RecentWatchListBox.SelectedItem, stock))
                    await LoadNewsForSelectedStockAsync(stock);
                else
                    RecentWatchListBox.SelectedItem = stock;
            }
            catch (Exception ex)
            {
                AppendLog($"stock search error: {query} / {ex.GetType().Name} / {ex.Message}");
            }
            finally
            {
                StockSearchButton.IsEnabled = true;
            }
        }

        private void HideStockSearchSuggestions()
        {
            _stockSearchSuggestions.Clear();
            StockSearchSuggestionListBox.Visibility = Visibility.Collapsed;
        }

        private void FocusSelectedRecentStock()
        {
            if (RecentWatchListBox.SelectedItem is null)
                return;

            RecentWatchListBox.UpdateLayout();
            if (RecentWatchListBox.ItemContainerGenerator.ContainerFromItem(RecentWatchListBox.SelectedItem) is ListBoxItem item)
            {
                item.Focus();
                return;
            }

            RecentWatchListBox.Focus();
        }

        private void CancelStockSearchSuggestion()
        {
            CancellationTokenSource? previous = _stockSearchSuggestionCts;
            _stockSearchSuggestionCts = null;
            if (previous is null)
                return;

            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                previous.Dispose();
            }
        }

        private async Task LoadNewsForSelectedStockAsync(object? selectedItem)
        {
            string stockName = selectedItem switch
            {
                WatchStockItem stock => stock.Name,
                ListBoxItem listBoxItem => listBoxItem.Tag as string ?? string.Empty,
                _ => string.Empty
            };
            string stockCode = selectedItem is WatchStockItem selectedWatch ? selectedWatch.Code : string.Empty;
            if (string.IsNullOrWhiteSpace(stockName))
                stockName = stockCode;
            if (string.IsNullOrWhiteSpace(stockName))
                return;

            string selectionKey = string.IsNullOrWhiteSpace(stockCode) ? stockName : stockCode;
            DateTime now = DateTime.Now;
            if (string.Equals(selectionKey, _lastAcceptedWatchSelectionKey, StringComparison.Ordinal) &&
                (now - _lastAcceptedWatchSelectionAt).TotalMilliseconds < DuplicateStockSelectionBlockMs)
            {
                return;
            }

            _lastAcceptedWatchSelectionKey = selectionKey;
            _lastAcceptedWatchSelectionAt = now;

            int selectionVersion = ++_selectionVersion;
            CancellationTokenSource? previousRequestCts = _selectedRequestCts;
            previousRequestCts?.Cancel();
            DisposeCanceledRequestLater(previousRequestCts);
            if (_watchStocks.Count > 0)
                ScheduleWatchlistBasePriceRefresh(_watchStocks, TimeSpan.FromSeconds(20));
            _selectedRequestCts = new CancellationTokenSource();
            CancellationToken requestToken = _selectedRequestCts.Token;
            SelectedStockTitle.Text = stockName;
            _selectedStockCode = stockCode;
            UpdateStrategyProgressRows();
            _recentTrades.Clear();
            _buyTradeVolume = 0;
            _sellTradeVolume = 0;
            _selectedPreviousVolume = 0;
            _selectedUsesUnifiedDailyVolume = false;
            _krxPrevClosePrice = 0;
            _lastTickPriceByCode.Clear();
            _currentChartCandles.Clear();
            _currentChartCode = string.Empty;
            _lastRealtimeChartDrawAt = DateTime.MinValue;
            ClearSelectedChartVisuals();
            HogaStatusText.Text = "Price - / Rate - / 0D -";
            UpdateHogaSummary(0, 0);
            ResetTradeSummaryInfo();
            InfoBasePriceText.Text = "-";
            InfoBasePriceText.Foreground = _whiteBrush;
            AppendLog($"stock selected: {stockName}");
            if (selectedItem is WatchStockItem recentStock)
                AddRecentViewedStock(recentStock);

            StartSelectedChartRender();
            _ = LoadNewsAsync(stockName, selectionVersion, requestToken);
            _ = LoadDisclosuresAsync(stockCode, selectionVersion, requestToken);
            await LoadSelectedBasePriceAsync(stockCode, selectionVersion, requestToken);
            if (!IsCurrentSelection(stockCode, selectionVersion))
                return;

            await LoadSelectedOrderBookSnapshotAsync(stockCode, selectionVersion, requestToken);
            if (!IsCurrentSelection(stockCode, selectionVersion))
                return;

            _ = RegisterSelectedRealtime0DIfReadyAsync();
            _ = LoadSelectedStockStatusAsync(stockCode, selectionVersion, requestToken);
            _ = LoadKrxClosingSnapshotIfNeededAsync(stockCode, selectionVersion, requestToken);
        }

        private void AddRecentViewedStock(WatchStockItem stock)
        {
            if (string.IsNullOrWhiteSpace(stock.Code))
                return;

            for (int i = _recentViewedStocks.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_recentViewedStocks[i].Code, stock.Code, StringComparison.Ordinal))
                    _recentViewedStocks.RemoveAt(i);
            }

            _recentViewedStocks.Insert(0, stock);
            while (_recentViewedStocks.Count > 30)
                _recentViewedStocks.RemoveAt(_recentViewedStocks.Count - 1);
        }

        private bool IsCurrentSelection(string stockCode, int selectionVersion)
        {
            return selectionVersion == _selectionVersion &&
                string.Equals(stockCode, _selectedStockCode, StringComparison.Ordinal);
        }

        private static void DisposeCanceledRequestLater(CancellationTokenSource? cts)
        {
            if (cts == null)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    cts.Dispose();
                }
            });
        }

        private void ClearSelectedChartVisuals()
        {
            MainChartHost?.Children.Clear();
            VolumeChartHost?.Children.Clear();
            _priceChartRenderState = null;
            _volumeChartRenderState = null;
            _lastCandleWick = null;
            _lastCandleBody = null;
            _lastVolumeBar = null;
            _currentPriceMarkerLine = null;
            _currentPriceMarkerLabel = null;
            _currentPriceMarkerText = null;
            _priceChartCanvas = null;
            _chartDragSelectionRect = null;
            _isChartDragSelecting = false;
            _chartViewStartIndex = 0;
            _chartViewCount = 0;
        }

        private async Task LoadSelectedBasePriceAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                if (selectionVersion != _selectionVersion || !_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                    return;

                string today = DateTime.Now.ToString("yyyyMMdd");
                WatchlistStockCacheEntry? cache = GetWatchlistMemoryCache(stockCode);
                // base price invariant: Query KRX base price only when a trusted cached previous close is unavailable.
                // Never replace it with another market, realtime, or stock-info value.
                long basePrice = IsTrustedKrxBasePrice(cache, today)
                    ? cache!.BasePrice
                    : await _kiwoomConditionService.GetKrxPreviousClosePriceAsync(stockCode, cancellationToken);
                if (selectionVersion != _selectionVersion)
                    return;

                if (basePrice > 0)
                {
                    WatchlistStockCacheEntry entry = UpsertWatchlistMemoryCache(
                        stockCode,
                        _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? stock) ? stock.Name : stockCode,
                        null,
                        stock?.MarketTypeCode,
                        stock?.MarketName,
                        stock?.ProgramMarketType,
                        stock?.CurrentPrice,
                        stock?.ChangeAmount,
                        stock?.ChangeRateText,
                        stock?.VolumeText,
                        stock?.LastPrice,
                        stock?.OrderWarning,
                        stock?.AuditInfo,
                        stock?.StockState,
                        stock?.SectorName,
                        saveFile: false);
                    SetCachedKrxBasePrice(entry, basePrice, today);
                }
                _krxPrevClosePrice = basePrice;
                InfoBasePriceText.Text = basePrice > 0 ? basePrice.ToString("N0") : "-";
                InfoBasePriceText.Foreground = _whiteBrush;
                AppendLog(basePrice > 0
                    ? $"base price locked(KRX prev close{(IsTrustedKrxBasePrice(cache, today) ? " cache" : "")}): {basePrice:N0}"
                    : "base price lock failed(KRX prev close, NXT fallback forbidden)");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"base price prime error: {ex.Message}");
            }
        }

        private async Task LoadNewsAsync(string stockName, int selectionVersion, CancellationToken cancellationToken)
        {
            try
            {
                var news = await _newsService.GetLatestNewsAsync(stockName, _config.Dashboard.NewsCount, cancellationToken);
                if (selectionVersion != _selectionVersion)
                    return;

                StockNewsListBox.ItemsSource = news;
                AppendLog($"news loaded: {stockName} ({news.Count}items)");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"news query error: {ex.Message}");
                MessageBox.Show($"news query error: {ex.Message}");
            }
        }

        private async Task LoadMarketNewsAsync(string? query = null, bool forceRefresh = false)
        {
            if (_isMarketNewsLoading)
                return;

            _isMarketNewsLoading = true;
            MarketNewsRefreshButton.IsEnabled = false;
            MarketNewsSearchButton.IsEnabled = false;
            string searchQuery = (query ?? MarketNewsSearchTextBox.Text).Trim();
            bool hasSearchQuery = !string.IsNullOrWhiteSpace(searchQuery);
            bool canUseCache = !hasSearchQuery
                && !forceRefresh
                && _marketNewsCache.Count > 0
                && DateTime.Now - _marketNewsCacheLoadedAt < TimeSpan.FromMinutes(10);

            if (canUseCache)
            {
                MarketNewsListBox.ItemsSource = _marketNewsCache.ToList();
                MarketNewsStatusText.Text = $"market news {_marketNewsCache.Count}items · cache";
                MarketNewsRefreshButton.IsEnabled = true;
                MarketNewsSearchButton.IsEnabled = true;
                _isMarketNewsLoading = false;
                return;
            }

            MarketNewsStatusText.Text = hasSearchQuery
                ? $"searching news: {searchQuery}"
                : "loading market news...";

            try
            {
                var news = hasSearchQuery
                    ? await _newsService.SearchNewsAsync(searchQuery)
                    : await _newsService.GetMarketNewsAsync();
                MarketNewsListBox.ItemsSource = news;
                if (!hasSearchQuery)
                {
                    _marketNewsCache.Clear();
                    _marketNewsCache.AddRange(news);
                    _marketNewsCacheLoadedAt = DateTime.Now;
                }

                MarketNewsStatusText.Text = hasSearchQuery
                    ? $"search {news.Count}items · {DateTime.Now:HH:mm} · {searchQuery}"
                    : $"market news {news.Count}items · {DateTime.Now:HH:mm} refreshed";
                AppendLog(hasSearchQuery
                    ? $"news search: {searchQuery} ({news.Count}items)"
                    : $"market news loaded: {news.Count}items");
                _ = LoadMarketNewsThumbnailsAsync(news);
            }
            catch (Exception ex)
            {
                if (!hasSearchQuery && _marketNewsCache.Count > 0)
                {
                    MarketNewsListBox.ItemsSource = _marketNewsCache.ToList();
                    MarketNewsStatusText.Text = $"market news {_marketNewsCache.Count}items · cached after error";
                }
                else
                {
                    MarketNewsListBox.ItemsSource = null;
                    MarketNewsStatusText.Text = hasSearchQuery ? "news search failed" : "market news query failed";
                }

                AppendLog($"{(hasSearchQuery ? "news search" : "market news query")} error: {ex.GetType().Name} / {ex.Message}");
            }
            finally
            {
                MarketNewsRefreshButton.IsEnabled = true;
                MarketNewsSearchButton.IsEnabled = true;
                _isMarketNewsLoading = false;
            }
        }

        private async void MarketNewsRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadMarketNewsAsync(forceRefresh: true);
        }

        private async void MarketNewsSearchButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadMarketNewsAsync(MarketNewsSearchTextBox.Text);
        }

        private async void MarketNewsSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await LoadMarketNewsAsync(MarketNewsSearchTextBox.Text);
        }

        private async void TelegramManualSendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendManualTelegramMessageAsync();
        }

        private async void TelegramManualTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            e.Handled = true;
            await SendManualTelegramMessageAsync();
        }

        private async Task SendManualTelegramMessageAsync()
        {
            string message = TelegramManualTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;

            TelegramManualSendButton.IsEnabled = false;
            try
            {
                await _telegramNotifier.SendManualToDefaultAsync(message);
                TelegramManualTextBox.Clear();
                AppendLog("Telegram manual message sent");
            }
            catch (Exception ex)
            {
                AppendLog($"Telegram manual message error: {ex.Message}");
            }
            finally
            {
                TelegramManualSendButton.IsEnabled = true;
            }
        }

        private async Task LoadMarketNewsThumbnailsAsync(IReadOnlyList<NewsItem> news)
        {
            using var gate = new SemaphoreSlim(3);
            var tasks = news
                .Where(item => !string.IsNullOrWhiteSpace(item.Link))
                .Select(async item =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        string thumbnailUrl = await _newsThumbnailService.GetThumbnailUrlAsync(item.Link).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(thumbnailUrl))
                            return;

                        await Dispatcher.InvokeAsync(() => item.ThumbnailUrl = thumbnailUrl);
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private void OpenLinkedItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            string link = string.Empty;
            if (sender is ListBox { SelectedItem: NewsItem news })
                link = news.Link;
            else if (sender is ListBox { SelectedItem: DisclosureItem disclosure })
                link = disclosure.Link;

            if (string.IsNullOrWhiteSpace(link))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = link,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"link open error: {ex.Message}");
            }
        }

        private async Task LoadDisclosuresAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                StockDisclosureListBox.ItemsSource = null;

                var disclosures = await _disclosureService.GetLatestDisclosuresAsync(stockCode, _config.Dashboard.DisclosureCount, cancellationToken);
                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                StockDisclosureListBox.ItemsSource = disclosures;
                AppendLog($"filings loaded: {stockCode} ({disclosures.Count}items)");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                if (selectionVersion != _selectionVersion)
                    return;
                StockDisclosureListBox.ItemsSource = null;
                AppendLog($"filings query error: {ex.GetType().Name} / {ex.Message}");
            }
        }

        private async Task LoadSelectedOrderBookSnapshotAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                if (selectionVersion != _selectionVersion || !_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                    return;

                bool useNxtMarket = ShouldUseNxtDataForStock(stockCode);

                KrxClosingSnapshot snapshot = await _kiwoomConditionService.GetOrderBookSnapshotAsync(stockCode, useNxtMarket, cancellationToken);
                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                if (!HasAnyHogaLevel(snapshot) && useNxtMarket && !IsNxtFrozenWindow())
                {
                    // When showing NXT/SOR, only KRX previous close is used as base price.
                    // KRX order-book fallback is skipped because it mixes values unlike the MTS NXT view.
                    AppendLog($"NXT order book empty, skip KRX fallback: {stockCode}");
                }

                if (!HasAnyHogaLevel(snapshot))
                {
                    if (TryApplyCurrentPriceFallbackHoga(stockCode, "order book REST fallback"))
                        return;

                    AppendLog($"order book REST empty, keep existing order book: {stockCode}");
                    return;
                }

                ApplyHogaRows(
                    [.. snapshot.SellLevels.Select(r => (r.Price, r.Quantity))],
                    [.. snapshot.BuyLevels.Select(r => (r.Price, r.Quantity))],
                    useNxtMarket ? "NXT order book REST" : "KRX order book REST");
                AppendLog($"{(useNxtMarket ? "NXT" : "KRX")} order book REST applied: {stockCode}");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"order book REST error: {ex.Message}");
            }
        }

        private async Task LoadSelectedStockStatusAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                if (selectionVersion != _selectionVersion)
                    return;

                if (!_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                {
                    SetSelectedStockSubInfo(new StockStatusMetrics(), "-", _whiteBrush);
                    return;
                }

                // MTS rule: base price always uses KRX previous close,
                // while NXT-enabled stocks show NXT OHLC/current price during NXT windows.
                bool useNxtMarket = ShouldUseNxtDataForStock(stockCode);
                StockStatusMetrics m = await _kiwoomConditionService.GetStockStatusMetricsByGuideAsync(stockCode, useNxtMarket, cancellationToken);
                if (useNxtMarket && IsEmptyStockStatus(m) && !IsNxtFrozenWindow())
                {
                    // When showing NXT/SOR, only KRX previous close is used as base price.
                    // Refilling OHLC/current price from KRX would diverge from MTS SOR ON.
                    AppendLog($"NXT stock metrics empty, skip KRX fallback: {stockCode}");
                }

                if (IsEmptyStockStatus(m))
                {
                    AppendLog($"stock metrics empty, keep existing metrics: {stockCode}");
                    return;
                }

                string statusRequestCode = useNxtMarket ? $"{NormalizeStockCode(stockCode)}_NX" : NormalizeStockCode(stockCode);
                _selectedUsesUnifiedDailyVolume = false;
                AppendLog(
                    $"stock metrics TR: {statusRequestCode} / {(useNxtMarket ? "NXT" : "KRX")} / " +
                    $"screen base(KRX) {(_krxPrevClosePrice > 0 ? _krxPrevClosePrice.ToString("N0") : "-")} / " +
                    $"response base {m.BasePriceText} / open {m.OpenPriceText} / high {m.HighPriceText} / low {m.LowPriceText} / close {m.ClosePriceText} / volume {m.VolumeText}");
                if (_watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selectedForCompare) && selectedForCompare.SupportsNxt)
                    await LogStockStatusCompareAsync(stockCode, selectionVersion, cancellationToken);

                _currentStatusMetrics = m;
                ApplySelectedWatchStockPriceInfo(stockCode, m);
                UpdateStrategyProgressRows();
                (string dailyVolumeRatioText, Brush dailyVolumeRatioBrush) = await GetDailyVolumeRatioAsync(stockCode, useNxtMarket, m, cancellationToken);
                if (selectionVersion != _selectionVersion)
                    return;

                m.VolumeRatioText = dailyVolumeRatioText;
                AppendLog(_krxPrevClosePrice > 0
                    ? $"order book base(KRX prev close): {_krxPrevClosePrice:N0}"
                    : "order book base(KRX prev close) query failed");
                SetSelectedStockSubInfo(m, dailyVolumeRatioText, dailyVolumeRatioBrush);
                InfoOpenPriceText.Text = m.OpenPriceText;
                InfoHighPriceText.Text = m.HighPriceText;
                InfoLowPriceText.Text = m.LowPriceText;
                // Do not use StockStatusMetrics.BasePriceText here.
                // Display the KRX previous close locked at selection start to avoid mixing NXT base values.
                InfoBasePriceText.Text = _krxPrevClosePrice > 0 ? _krxPrevClosePrice.ToString("N0") : "-";
                ApplySelectedPriceInfoColors(m);
                InfoTradingValueText.Text = m.TradingValueText;
                InfoVolumeText.Text = m.VolumeText;
                InfoPrevTimeVolumeRatioText.Text = m.VolumeRatioText;
                InfoPrevTimeVolumeRatioText.Foreground = dailyVolumeRatioBrush;
                InfoTurnoverRateText.Text = m.TurnoverRateText;
                StockStatusMetrics exec = await LoadExecutionSummaryByMarketAsync(stockCode, useNxtMarket, cancellationToken);
                if (exec.BuyExecCum > 0 || exec.SellExecCum > 0)
                {
                    _buyTradeVolume = Math.Max(0, exec.BuyExecCum);
                    _sellTradeVolume = Math.Max(0, exec.SellExecCum);
                    _lastBuyExecCumByCode[stockCode] = _buyTradeVolume;
                    _lastSellExecCumByCode[stockCode] = _sellTradeVolume;
                    UpdateTradeSummaryInfo();
                }
                else
                {
                    ResetTradeSummaryInfo();
                    AppendLog($"execution volume empty or total mismatch, hide execution volume: {stockCode}");
                }
                if (ShouldUseFinalDailyVolumeForRatio(stockCode, useNxtMarket) && (exec.DailyTradeQty > 0 || exec.DailySectionTradeQty > 0))
                {
                    long verifiedVolume = exec.DailyTradeQty > 0 ? exec.DailyTradeQty : exec.DailySectionTradeQty;
                    InfoVolumeText.Text = verifiedVolume.ToString("N0");
                    m.VolumeText = InfoVolumeText.Text;
                    if (exec.DailyTradeValueMillion > 0)
                    {
                        m.TradingValueText = FormatMillionWonUnit(exec.DailyTradeValueMillion);
                        InfoTradingValueText.Text = m.TradingValueText;
                    }
                    m.TurnoverRateText = FormatTurnoverRate(verifiedVolume, ParseLongAbs(m.ListedSharesText));
                    InfoTurnoverRateText.Text = m.TurnoverRateText;
                    (string verifiedVolumeRatioText, Brush verifiedVolumeRatioBrush) = FormatDailyVolumeRatio(verifiedVolume);
                    m.VolumeRatioText = verifiedVolumeRatioText;
                    _selectedUsesUnifiedDailyVolume = true;
                    InfoPrevTimeVolumeRatioText.Text = verifiedVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Foreground = verifiedVolumeRatioBrush;
                    AppendLog($"unified daily trade detail total: total {verifiedVolume:N0} / pre {exec.BeforeMarketTradeQty:N0} / regular {exec.RegularMarketTradeQty:N0} / after {exec.AfterMarketTradeQty:N0}");
                }
                else if (exec.DailyTradeQty > 0 || exec.DailySectionTradeQty > 0)
                {
                    AppendLog($"keep regular-session volume ratio based on current cumulative volume: {stockCode}");
                }

                // Realtime 0B updates redraw the program field from _currentStatusMetrics.
                // Store the ka90008 program value in the current stock state, not only in the UI.
                m.ProgramBuyText = exec.ProgramBuyText;
                m.ProgramNetQuantity = exec.ProgramNetQuantity;
                m.HasProgramTrade = exec.HasProgramTrade;
                _currentStatusMetrics.ProgramBuyText = exec.ProgramBuyText;
                _currentStatusMetrics.ProgramNetQuantity = exec.ProgramNetQuantity;
                _currentStatusMetrics.HasProgramTrade = exec.HasProgramTrade;
                ApplyProgramTradeInfo(_currentStatusMetrics);
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"status metrics query error: {ex.Message}");
            }
        }

        private async Task<(string Text, Brush Brush)> GetDailyVolumeRatioAsync(string stockCode, bool useNxtMarket, StockStatusMetrics metrics, CancellationToken cancellationToken = default)
        {
            long todayVolume = ParseLongAbs(metrics.VolumeText);
            if (todayVolume <= 0)
                return ("-", _whiteBrush);

            IReadOnlyList<(string Date, long Volume)> volumes = await _kiwoomConditionService.GetUnifiedDailyTradeVolumesAsync(stockCode, 5, cancellationToken);
            List<(string Date, long Volume)> orderedVolumes = [.. volumes
                .Where(v => !string.IsNullOrWhiteSpace(v.Date) && v.Volume > 0)
                .OrderByDescending(v => v.Date)];

            long previousVolume = orderedVolumes.Count > 1 ? orderedVolumes[1].Volume : 0;
            _selectedPreviousVolume = previousVolume;
            if (previousVolume <= 0)
                return ("-", _whiteBrush);

            return FormatDailyVolumeRatio(todayVolume);
        }

        private static DailyCandle? ResolvePreviousVolumeCandle(IReadOnlyList<DailyCandle> orderedCandles)
        {
            return orderedCandles.Count > 1 ? orderedCandles[1] : null;
        }

        private async Task LogStockStatusCompareAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<(string RequestCode, StockStatusMetrics Metrics)> rows =
                    await _kiwoomConditionService.GetStockStatusMetricsCompareAsync(stockCode, cancellationToken);

                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                foreach ((string requestCode, StockStatusMetrics metrics) in rows)
                {
                    AppendLog(
                        $"stock metrics compare: {requestCode} / base {metrics.BasePriceText} / " +
                        $"open {metrics.OpenPriceText} / high {metrics.HighPriceText} / low {metrics.LowPriceText} / close {metrics.ClosePriceText} / " +
                        $"volume {metrics.VolumeText} / value {metrics.TradingValueText}");
                }
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                if (selectionVersion == _selectionVersion && stockCode == _selectedStockCode)
                    AppendLog($"stock metrics compare log error: {stockCode} / {ex.Message}");
            }
        }

        private async Task<StockStatusMetrics> LoadExecutionSummaryByMarketAsync(string stockCode, bool useNxtMarketNow, CancellationToken cancellationToken)
        {
            bool supportsNxt = _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected) && selected.SupportsNxt;
            string programMarketType = selected?.ProgramMarketType ?? string.Empty;
            bool useNxtExecution = supportsNxt && (useNxtMarketNow || IsNxtFrozenWindow());
            StockStatusMetrics exec = await _kiwoomConditionService.GetTodayExecutionSummaryAsync(stockCode, useNxtExecution, programMarketType, cancellationToken);
            if (!useNxtExecution || HasAnyExecutionSummaryValue(exec) || IsNxtFrozenWindow())
                return exec;

            // When showing NXT/SOR, do not mix KRX execution/quote values except KRX previous close.
            AppendLog($"NXT execution summary empty, skip KRX fallback: {stockCode}");
            return exec;
        }

        private bool ShouldUseFinalDailyVolumeForRatio(string stockCode, bool useNxtMarket)
        {
            bool supportsNxt = IsNxtSupportedStock(stockCode);
            if (supportsNxt && useNxtMarket)
            {
                // MTS SOR ON rule: show NXT OHLC in NXT windows,
                // but prefer _AL unified daily details for volume/value/previous-volume ratio.
                return IsNxtMarketWindow() || IsNxtFrozenWindow();
            }

            return IsKrxRegularClosedWindow();
        }

        private static bool HasAnyExecutionSummaryValue(StockStatusMetrics metrics)
        {
            return metrics.BuyExecCum > 0
                || metrics.SellExecCum > 0
                || metrics.DailyTradeQty > 0
                || metrics.BeforeMarketTradeQty > 0
                || metrics.RegularMarketTradeQty > 0
                || metrics.AfterMarketTradeQty > 0
                || metrics.DailySectionTradeQty > 0
                || metrics.HasProgramTrade;
        }

        private void ApplyProgramTradeInfo(StockStatusMetrics metrics)
        {
            if (!metrics.HasProgramTrade)
            {
                InfoProgramText.Text = "-";
                InfoProgramText.Foreground = _whiteBrush;
                return;
            }

            InfoProgramText.Text = metrics.ProgramBuyText;
            InfoProgramText.Foreground = metrics.ProgramNetQuantity > 0
                ? _upColorBrush
                : metrics.ProgramNetQuantity < 0 ? _downColorBrush : _whiteBrush;
        }

        private (string Text, Brush Brush) FormatDailyVolumeRatio(long todayVolume)
        {
            if (todayVolume <= 0 || _selectedPreviousVolume <= 0)
                return ("-", _whiteBrush);

            decimal ratio = todayVolume / (decimal)_selectedPreviousVolume * 100m;
            decimal displayRatio = Math.Truncate(ratio * 100m) / 100m;
            Brush brush = todayVolume > _selectedPreviousVolume ? _upColorBrush : todayVolume < _selectedPreviousVolume ? _downColorBrush : _whiteBrush;
            return ($"{displayRatio:0.00}%", brush);
        }

        private static string FormatTurnoverRate(long volume, long listedShares)
        {
            if (volume <= 0 || listedShares <= 0)
                return "-";

            decimal rate = volume / (decimal)listedShares * 100m;
            decimal displayRate = Math.Truncate(rate * 100m) / 100m;
            return $"{displayRate:0.00}%";
        }

        private void SetSelectedStockSubInfo(StockStatusMetrics metrics, string volumeRatioText, Brush volumeRatioBrush)
        {
            SelectedStockSubInfo.Inlines.Clear();
            if (!string.IsNullOrWhiteSpace(_selectedStockCode)
                && _watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? stock)
                && stock.MetaBadgeText != "-")
            {
                SelectedStockSubInfo.Inlines.Add(new Run($"{stock.MetaBadgeText} · "));
                SelectedStockSubInfo.Inlines.Add(new Run($"Prev Close {(_krxPrevClosePrice > 0 ? _krxPrevClosePrice.ToString("N0") : "-")} · "));
            }

            SelectedStockSubInfo.Inlines.Add(new Run(
                $"Market Cap {metrics.MarketCapText} · Float Shares {metrics.ListedSharesText}"));
        }

        private async Task LoadKrxClosingSnapshotIfNeededAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                    return;
                if (selectionVersion != _selectionVersion)
                    return;

                bool supportsNxt = _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected)
                    && selected.SupportsNxt;
                bool useNxtSnapshot = supportsNxt && IsNxtFrozenWindow();

                if (supportsNxt && IsNxtMarketWindow() && !useNxtSnapshot)
                {
                    AppendLog($"NXT session active: skip KRX close snapshot: {stockCode}");
                    return;
                }

                if (!IsKrxRegularClosedWindow() && !useNxtSnapshot)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                string cacheKey = BuildClosingSnapshotCacheKey(stockCode, useNxtSnapshot);
                if (_closingSnapshotMemoryCache.TryGetValue(cacheKey, out ClosingSnapshotCacheEntry? cached))
                {
                    ApplyClosingSnapshot(CloneClosingSnapshot(cached.Snapshot), cached.IsNxtSnapshot ? "NXT 20:00 final cache" : "KRX close cache");
                    AppendLog($"{(cached.IsNxtSnapshot ? "NXT 20:00 final" : "KRX close")} snapshot cache applied: {stockCode}");
                    return;
                }

                KrxClosingSnapshot snapshot = await _kiwoomConditionService.GetKrxClosingSnapshotAsync(stockCode, useNxtSnapshot, cancellationToken);
                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                snapshot.BuyExecCum = 0;
                snapshot.SellExecCum = 0;

                _closingSnapshotMemoryCache[cacheKey] = new ClosingSnapshotCacheEntry
                {
                    Snapshot = CloneClosingSnapshot(snapshot),
                    IsNxtSnapshot = useNxtSnapshot,
                    CachedAt = DateTime.Now
                };
                ApplyClosingSnapshot(snapshot, useNxtSnapshot ? "NXT 20:00 final" : "KRX close");
                AppendLog($"{(useNxtSnapshot ? "NXT 20:00 final" : "KRX close")} snapshot applied: {stockCode}");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"closing snapshot error: {ex.Message}");
            }
        }

        private void ApplyClosingSnapshot(KrxClosingSnapshot snapshot, string source)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Code) || snapshot.Code != _selectedStockCode)
                return;

            if (_watchStockByCode.TryGetValue(snapshot.Code, out WatchStockItem? stock))
            {
                if (snapshot.CurrentPrice > 0)
                    stock.CurrentPrice = snapshot.CurrentPrice;
                stock.ChangeAmount = stock.CurrentPrice > 0 && _krxPrevClosePrice > 0
                    ? stock.CurrentPrice - _krxPrevClosePrice
                    : snapshot.DayChange;
                stock.ChangeRateText = FormatKrxPreviousCloseRate(stock.CurrentPrice);
                stock.PriceBrush = ResolveHogaBrushByKrxPrevClose(stock.CurrentPrice);
            }

            if (HasAnyHogaLevel(snapshot))
                ApplyHogaRows([.. snapshot.SellLevels.Select(r => (r.Price, r.Quantity))], [.. snapshot.BuyLevels.Select(r => (r.Price, r.Quantity))], $"{source} snapshot");
            else
                AppendLog($"{source} snapshot order book empty, keep existing order book: {snapshot.Code}");
            if (snapshot.CurrentPrice > 0)
            {
                InfoBasePriceText.Text = _krxPrevClosePrice > 0 ? _krxPrevClosePrice.ToString("N0") : "-";
                InfoBasePriceText.Foreground = _whiteBrush;
            }
            if (snapshot.DailyTradeQty > 0 || snapshot.DailySectionTradeQty > 0)
            {
                long verifiedVolume = snapshot.DailyTradeQty > 0 ? snapshot.DailyTradeQty : snapshot.DailySectionTradeQty;
                InfoVolumeText.Text = verifiedVolume.ToString("N0");
                if (snapshot.DailyTradeValueMillion > 0)
                {
                    _currentStatusMetrics.TradingValueText = FormatMillionWonUnit(snapshot.DailyTradeValueMillion);
                    InfoTradingValueText.Text = _currentStatusMetrics.TradingValueText;
                }
                (string verifiedVolumeRatioText, Brush verifiedVolumeRatioBrush) = FormatDailyVolumeRatio(verifiedVolume);
                InfoPrevTimeVolumeRatioText.Text = verifiedVolumeRatioText;
                InfoPrevTimeVolumeRatioText.Foreground = verifiedVolumeRatioBrush;
                _currentStatusMetrics.VolumeRatioText = verifiedVolumeRatioText;
                if (_watchStockByCode.TryGetValue(snapshot.Code, out WatchStockItem? stockForVolume))
                    stockForVolume.VolumeText = InfoVolumeText.Text;
                AppendLog($"{source} unified daily trade detail total: total {verifiedVolume:N0} / pre {snapshot.BeforeMarketTradeQty:N0} / regular {snapshot.RegularMarketTradeQty:N0} / after {snapshot.AfterMarketTradeQty:N0}");
            }

            ResetTradeSummaryInfo();
            AppendLog($"{source} post-close buy/sell execution volume blanked: {snapshot.Code}");

            if (snapshot.RecentTrades.Count == 0)
            {
                AppendLog($"{source} no recent trades, keep existing trade list: {snapshot.Code}");
                return;
            }

            _recentTrades.Clear();
            foreach (ClosingTradePrint trade in snapshot.RecentTrades.Take(10))
            {
                _recentTrades.Add(new TradePrint
                {
                    PriceText = trade.Price > 0 ? trade.Price.ToString("N0") : "-",
                    QuantityText = trade.Quantity > 0 ? trade.Quantity.ToString("N0") : "-",
                    Color = ResolveHogaBrushByKrxPrevClose(trade.Price),
                    QuantityColor = trade.IsBuyAggressive ? _upColorBrush : _downColorBrush
                });
            }
        }

        private static string BuildClosingSnapshotCacheKey(string stockCode, bool useNxtSnapshot)
        {
            return $"{DateTime.Now:yyyyMMdd}|{stockCode}|{(useNxtSnapshot ? "NXT" : "KRX")}";
        }

        private static KrxClosingSnapshot CloneClosingSnapshot(KrxClosingSnapshot source)
        {
            var clone = new KrxClosingSnapshot
            {
                Code = source.Code,
                CurrentPrice = source.CurrentPrice,
                BasePrice = source.BasePrice,
                DayChange = source.DayChange,
                ChangeRateText = source.ChangeRateText,
                BuyExecCum = source.BuyExecCum,
                SellExecCum = source.SellExecCum,
                DailyTradeQty = source.DailyTradeQty,
                DailyTradeValueMillion = source.DailyTradeValueMillion,
                BeforeMarketTradeQty = source.BeforeMarketTradeQty,
                RegularMarketTradeQty = source.RegularMarketTradeQty,
                AfterMarketTradeQty = source.AfterMarketTradeQty,
                DailySectionTradeQty = source.DailySectionTradeQty
            };

            foreach (HogaQuoteLevel level in source.SellLevels)
                clone.SellLevels.Add(new HogaQuoteLevel { Price = level.Price, Quantity = level.Quantity });
            foreach (HogaQuoteLevel level in source.BuyLevels)
                clone.BuyLevels.Add(new HogaQuoteLevel { Price = level.Price, Quantity = level.Quantity });
            foreach (ClosingTradePrint trade in source.RecentTrades)
                clone.RecentTrades.Add(new ClosingTradePrint { Price = trade.Price, Quantity = trade.Quantity, IsBuyAggressive = trade.IsBuyAggressive });

            return clone;
        }

        private static bool HasAnyHogaLevel(KrxClosingSnapshot snapshot)
        {
            return snapshot != null &&
                (snapshot.SellLevels.Any(r => r.Price > 0 || r.Quantity > 0) ||
                 snapshot.BuyLevels.Any(r => r.Price > 0 || r.Quantity > 0));
        }

        private void UpdateTradeSummaryInfo()
        {
            long total = _buyTradeVolume + _sellTradeVolume;
            decimal buyRatio = total > 0 ? (decimal)_buyTradeVolume / total * 100m : 0m;

            InfoBuyTradeVolumeText.Text = _buyTradeVolume.ToString("N0");
            InfoSellTradeVolumeText.Text = _sellTradeVolume.ToString("N0");
            InfoBuyTradeVolumeText.Foreground = _upColorBrush;
            InfoSellTradeVolumeText.Foreground = _downColorBrush;
            InfoBuyRatioText.Text = total > 0 ? $"{buyRatio:0}%" : "-";
        }

        private void ResetTradeSummaryInfo()
        {
            InfoBuyTradeVolumeText.Text = "-";
            InfoSellTradeVolumeText.Text = "-";
            InfoBuyTradeVolumeText.Foreground = _upColorBrush;
            InfoSellTradeVolumeText.Foreground = _downColorBrush;
            InfoBuyRatioText.Text = "-";
        }

        private void ApplySelectedPriceInfoColors(StockStatusMetrics metrics)
        {
            InfoOpenPriceText.Foreground = ResolveHogaBrushByKrxPrevClose(ParseLongAbs(metrics.OpenPriceText));
            InfoHighPriceText.Foreground = ResolveHogaBrushByKrxPrevClose(ParseLongAbs(metrics.HighPriceText));
            InfoLowPriceText.Foreground = ResolveHogaBrushByKrxPrevClose(ParseLongAbs(metrics.LowPriceText));
            InfoBasePriceText.Foreground = _whiteBrush;
        }

        private void UpdateHogaSummary(long? totalSell, long? totalBuy)
        {
            if (!totalSell.HasValue || !totalBuy.HasValue)
            {
                HogaTotalSellText.Text = "-";
                HogaTotalBuyText.Text = "-";
                HogaDiffText.Text = "-";
                HogaTotalSellText.Foreground = (Brush)FindResource("PaletteBlueGray");
                HogaTotalBuyText.Foreground = (Brush)FindResource("PaletteBlueGray");
                HogaDiffText.Foreground = (Brush)FindResource("PaletteBlueGray");
                return;
            }

            long sell = Math.Max(0, totalSell.Value);
            long buy = Math.Max(0, totalBuy.Value);
            long diff = sell - buy;

            HogaTotalSellText.Text = sell.ToString("N0");
            HogaTotalBuyText.Text = buy.ToString("N0");
            HogaDiffText.Text = Math.Abs(diff).ToString("N0");
            HogaTotalSellText.Foreground = (Brush)FindResource("PaletteBlueGray");
            HogaTotalBuyText.Foreground = (Brush)FindResource("PaletteBlueGray");
            HogaDiffText.Foreground = diff > 0 ? _downColorBrush : diff < 0 ? _upColorBrush : (Brush)FindResource("PaletteBlueGray");
        }

        private static bool IsEmptyStockStatus(StockStatusMetrics metrics)
        {
            return metrics == null ||
                (ParseLongAbs(metrics.ClosePriceText) <= 0 &&
                 ParseLongAbs(metrics.OpenPriceText) <= 0 &&
                 ParseLongAbs(metrics.HighPriceText) <= 0 &&
                 ParseLongAbs(metrics.LowPriceText) <= 0 &&
                 ParseLongAbs(metrics.VolumeText) <= 0);
        }

        private void ApplySelectedWatchStockPriceInfo(string stockCode, StockStatusMetrics metrics)
        {
            if (string.IsNullOrWhiteSpace(stockCode) || !_watchStockByCode.TryGetValue(stockCode, out WatchStockItem? stock))
                return;

            long currentPrice = ParseLongAbs(metrics.ClosePriceText);
            if (currentPrice > 0)
                stock.CurrentPrice = currentPrice;

            long basePrice = _krxPrevClosePrice;

            long changeAmount = currentPrice > 0 && basePrice > 0
                ? currentPrice - basePrice
                : ParseLongSigned(metrics.PrevDiffText);

            stock.ChangeAmount = changeAmount;
            stock.ChangeRateText = FormatKrxPreviousCloseRate(currentPrice);
            stock.PriceBrush = currentPrice > 0
                ? ResolveHogaBrushByKrxPrevClose(currentPrice)
                : changeAmount > 0 ? _upColorBrush : changeAmount < 0 ? _downColorBrush : _whiteBrush;

            string rateText = stock.ChangeRateText;
            HogaStatusText.Text = $"Price {(currentPrice > 0 ? currentPrice.ToString("N0") : stock.CurrentPrice > 0 ? stock.CurrentPrice.ToString("N0") : "-")} / Rate {rateText} / Base {(basePrice > 0 ? basePrice.ToString("N0") : "-")}";
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logLines.Enqueue(new LogLineEntry(line, false));

            while (_logLines.Count > MaxLogLines)
            {
                _logLines.Dequeue();
            }

            RenderLogLines();
        }

        private void AppendReadyLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendReadyLog(message));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logLines.Enqueue(new LogLineEntry(line, true));

            while (_logLines.Count > MaxLogLines)
            {
                _logLines.Dequeue();
            }

            RenderLogLines();
        }

        private void RenderLogLines()
        {
            LeftLogTextBox.Document.Blocks.Clear();
            Brush normalLogBrush = (Brush)FindResource("TextSubBrush");

            foreach (LogLineEntry entry in _logLines)
                LeftLogTextBox.Document.Blocks.Add(CreateLogParagraph(entry, normalLogBrush));

            LeftLogTextBox.ScrollToEnd();
        }

        private static Paragraph CreateLogParagraph(LogLineEntry entry, Brush normalLogBrush)
        {
            var paragraph = new Paragraph(new Run(entry.Text))
            {
                Margin = new Thickness(0),
                LineHeight = 14,
                Foreground = entry.IsReady ? new SolidColorBrush(Color.FromRgb(183, 255, 74)) : normalLogBrush,
                FontWeight = entry.IsReady ? FontWeights.Bold : FontWeights.Normal
            };

            if (entry.IsReady)
                paragraph.TextEffects = CreateReadyLogTextEffects(entry.Text.Length);

            return paragraph;
        }

        private static TextEffectCollection CreateReadyLogTextEffects(int textLength) =>
        [
            new()
            {
                Foreground = new SolidColorBrush(Color.FromArgb(150, 183, 255, 74)),
                PositionStart = 0,
                PositionCount = textLength
            }
        ];

        private void LeftLogTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _logLines.Clear();
            AppendLog("Log cleared");
        }

        private sealed record LogLineEntry(string Text, bool IsReady);

        private void SetStartupLoading(bool isVisible, string message, string statusLine1 = "", string statusLine2 = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStartupLoading(isVisible, message, statusLine1, statusLine2));
                return;
            }

            StartupLoadingOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            StartupLoadingText.Text = string.IsNullOrWhiteSpace(message) ? "Loading..." : message;
            StartupStatusLine1Text.Text = string.IsNullOrWhiteSpace(statusLine1) ? "Working..." : statusLine1;
            StartupStatusLine2Text.Text = string.IsNullOrWhiteSpace(statusLine2) ? "Please wait" : statusLine2;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _selectedRequestCts?.Cancel();
                _selectedRequestCts?.Dispose();
                _chartRequestCts?.Cancel();
                _chartRequestCts?.Dispose();
                _watchlistCacheRefreshCts?.Cancel();
                _watchlistCacheRefreshCts?.Dispose();
                _strategyMinuteAutoPreloadCts?.Cancel();
                _strategyMinuteAutoPreloadCts?.Dispose();
                CancelStockSearchSuggestion();
                _balanceRequestCts?.Cancel();
                _balanceRequestCts?.Dispose();
                _realtimeCts?.Cancel();
                _realtimeWs?.Abort();
                _realtimeWs?.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private static string NormalizeStockCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string text = raw.Trim().Replace("_AL", "", StringComparison.OrdinalIgnoreCase).Replace("_NX", "", StringComparison.OrdinalIgnoreCase);
            if (text.StartsWith("A", StringComparison.OrdinalIgnoreCase))
                text = text[1..];

            string digits = new([.. text.Where(char.IsDigit)]);
            if (digits.Length == 0)
                return string.Empty;

            return digits.Length >= 6 ? digits[^6..] : digits.PadLeft(6, '0');
        }

        private static long ParseLongAbs(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string clean = text.Replace(",", "").Replace("+", "").Trim();
            return long.TryParse(clean, out long value) ? Math.Abs(value) : 0;
        }

        private static long ParseLongSigned(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string clean = text.Replace(",", "").Trim();
            return long.TryParse(clean, out long value) ? value : 0;
        }

        private static long ResolveChangeByRate(string rateText)
        {
            if (string.IsNullOrWhiteSpace(rateText) || rateText == "-")
                return 0;

            string clean = rateText.Replace("%", "").Trim();
            if (!decimal.TryParse(clean, out decimal rate))
                return 0;

            if (rate > 0) return 1;
            if (rate < 0) return -1;
            return 0;
        }

        private sealed record BaseCandleGateResult(
            int Offset,
            string Date,
            string Market,
            double ChangeRate,
            long TradeValue);

    }
}
