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

namespace TradingDashboard
{
    public partial class MainWindow : Window
    {
        private const int MaxLogLines = 500;
        private const int MaxWatchlistMemoryCacheCount = 200;
        private const string KrxPreviousCloseBasePriceSource = "KRX_PREV_CLOSE";
        private const double ChartRightPadding = 25d;
        private readonly AppConfig _config;
        private readonly NaverNewsService _newsService;
        private readonly DartDisclosureService _disclosureService;
        private readonly KiwoomRestConditionService _kiwoomConditionService;
        private readonly WatchlistStockCacheStore _watchlistCacheStore = new WatchlistStockCacheStore();
        private readonly Queue<string> _logLines = new Queue<string>();
        private readonly Brush _upColorBrush;
        private readonly Brush _downColorBrush;
        private readonly Brush _aggressiveBuyQtyBrush;
        private readonly Brush _rateUpBrush;
        private readonly Brush _rateDownBrush;
        private readonly Brush _whiteBrush;
        private readonly ObservableCollection<WatchStockItem> _watchStocks = new ObservableCollection<WatchStockItem>();
        private readonly ObservableCollection<TradePrint> _recentTrades = new ObservableCollection<TradePrint>();
        private readonly ObservableCollection<HogaLevel> _sellHogaLevels = new ObservableCollection<HogaLevel>();
        private readonly ObservableCollection<HogaLevel> _buyHogaLevels = new ObservableCollection<HogaLevel>();
        private readonly Dictionary<string, WatchStockItem> _watchStockByCode = new Dictionary<string, WatchStockItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, WatchlistStockCacheEntry> _watchlistMemoryCache = new Dictionary<string, WatchlistStockCacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, ClosingSnapshotCacheEntry> _closingSnapshotMemoryCache = new Dictionary<string, ClosingSnapshotCacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastTickPriceByCode = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastBuyExecCumByCode = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _lastSellExecCumByCode = new Dictionary<string, long>(StringComparer.Ordinal);
        private const int MinuteChartCandleCount = 700;
        private const int DailyChartRealtimeDrawIntervalMs = 350;
        private const int MinuteChartRealtimeDrawIntervalMs = 1500;
        private const int MaxChartMemoryCacheEntries = 200;
        private const int MaxChartMemoryCacheCandles = 100_000;
        private const int MinChartDragCandleCount = 10;
        private readonly List<ChartCandle> _currentChartCandles = new List<ChartCandle>();
        private readonly Dictionary<ChartCacheKey, ChartCacheEntry> _chartMemoryCache = new Dictionary<ChartCacheKey, ChartCacheEntry>();
        private long _chartCacheAccessSequence;
        private int _chartRenderVersion;
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
        private StockStatusMetrics _currentStatusMetrics = new StockStatusMetrics();
        private long _krxPrevClosePrice;
        private long _selectedPreviousVolume;
        private int _selectionVersion;
        private DateTime _last0DReceivedAt = DateTime.MinValue;
        private string _lastMarketStatusCode = string.Empty;
        private string _lastMarketStatusTime = string.Empty;
        private string _lastMarketExpectedRemain = string.Empty;
        private string _lastMarketStatusText = "장상태 미확인";
        private string _conditionRealtimeSeq = string.Empty;
        private DateTime _lastMarketStatusAt = DateTime.MinValue;
        private DateTime _marketStatusUnknownUntil = DateTime.MinValue;
        private bool _isNxtMarketMode;
        private ClientWebSocket? _realtimeWs;
        private CancellationTokenSource? _realtimeCts;
        private CancellationTokenSource? _selectedRequestCts;
        private CancellationTokenSource? _chartRequestCts;
        private CancellationTokenSource? _watchlistCacheRefreshCts;
        private static readonly string[] MarketStatusTypes = { "0s" };
        private static readonly string[] SellPriceKeys = { "41", "42", "43", "44", "45", "46", "47", "48", "49", "50" };
        private static readonly string[] SellQtyKeys = { "61", "62", "63", "64", "65", "66", "67", "68", "69", "70" };
        private static readonly string[] BuyPriceKeys = { "51", "52", "53", "54", "55", "56", "57", "58", "59", "60" };
        private static readonly string[] BuyQtyKeys = { "71", "72", "73", "74", "75", "76", "77", "78", "79", "80" };

        private sealed class ClosingSnapshotCacheEntry
        {
            public KrxClosingSnapshot Snapshot { get; set; } = new KrxClosingSnapshot();
            public bool IsNxtSnapshot { get; set; }
            public DateTime CachedAt { get; set; } = DateTime.Now;
        }

        public MainWindow()
        {
            InitializeComponent();

            _upColorBrush = (Brush)FindResource("PaletteRed");
            _downColorBrush = (Brush)FindResource("PaletteBlue");
            _aggressiveBuyQtyBrush = (Brush)FindResource("PalettePink");
            _rateUpBrush = (Brush)FindResource("PalettePink");
            _rateDownBrush = (Brush)FindResource("PaletteSkyBlue");
            _whiteBrush = (Brush)FindResource("PaletteWhite");

            _config = LocalSettingsLoader.Load();
            _newsService = new NaverNewsService(_config.NaverNews);
            _disclosureService = new DartDisclosureService(_config.Dart);
            _kiwoomConditionService = new KiwoomRestConditionService(_config.Kiwoom);
            _kiwoomConditionService.RestLimitLog += message => Dispatcher.Invoke(() => AppendLog(message));
            LoadWatchlistCache();
            WatchListBox.ItemsSource = _watchStocks;
            RecentTradeListBox.ItemsSource = _recentTrades;
            SellQtyListBox.ItemsSource = _sellHogaLevels;
            SellPriceListBox.ItemsSource = _sellHogaLevels;
            BuyPriceListBox.ItemsSource = _buyHogaLevels;
            BuyQtyListBox.ItemsSource = _buyHogaLevels;
            for (int i = 0; i < 10; i++)
            {
                _sellHogaLevels.Add(new HogaLevel());
                _buyHogaLevels.Add(new HogaLevel());
            }
            HogaStatusText.Text = "현재가 - / 등락률 - / 0D -";
            UpdateHogaSummary(0, 0);

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppendLog("앱 시작");
            try
            {
                SetStartupLoading(true, "장구분 확인 중...");
                await PrimeMarketStatusBeforeWatchlistAsync();

                SetStartupLoading(true, "검색식 01번에서 종목 불러오는 중...");
                await LoadWatchListFromKiwoomConditionAsync();
            }
            finally
            {
                SetStartupLoading(false, string.Empty);
            }

            if (WatchListBox.SelectedItem is not ListBoxItem)
            {
                WatchListBox.SelectedIndex = 0;
            }
        }

        private async Task PrimeMarketStatusBeforeWatchlistAsync()
        {
            if (!_config.Kiwoom.UseRestApi)
                return;

            try
            {
                AppendLog("0s 장운영구분 선조회 시작");
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
                    AppendLog($"0s 선조회 LOGIN 실패: {loginCode}");
                    return;
                }

                await RegisterMarketStatusAsync(ws, ct);
                MarketStatusSnapshot status = await ReceiveMarketStatusSnapshotAsync(ws, ct);
                if (string.IsNullOrWhiteSpace(status.Code))
                {
                    MarkMarketStatusUnknown();
                    AppendLog("0s 장운영구분 선조회값 없음, 장상태 미확인(KRX 우선)");
                    return;
                }

                ApplyMarketStatusSnapshot(status, allowRefresh: false);
                AppendLog($"0s 장운영구분 선조회: 215={_lastMarketStatusCode} / {_lastMarketStatusText} / {(_isNxtMarketMode ? "NXT 사용" : "KRX 사용")}");
            }
            catch (OperationCanceledException)
            {
                MarkMarketStatusUnknown();
                AppendLog("0s 장운영구분 선조회 시간초과, 장상태 미확인(KRX 우선)");
            }
            catch (Exception ex)
            {
                MarkMarketStatusUnknown();
                AppendLog($"0s 장운영구분 선조회 오류: {ex.Message}");
            }
        }

        private void MarkMarketStatusUnknown()
        {
            _lastMarketStatusCode = string.Empty;
            _lastMarketStatusTime = string.Empty;
            _lastMarketExpectedRemain = string.Empty;
            _lastMarketStatusText = "장상태 미확인";
            _lastMarketStatusAt = DateTime.MinValue;
            _marketStatusUnknownUntil = DateTime.Now.AddMinutes(2);
            _isNxtMarketMode = false;
        }

        private async Task LoadWatchListFromKiwoomConditionAsync()
        {
            try
            {
                if (!_config.Kiwoom.UseRestApi)
                {
                    AppendLog("키움 REST 비활성화(UseRestApi=false)");
                    _krxPrevClosePrice = 0;
                    return;
                }

                AppendLog("키움 조건식(01) 조회 시작");

                List<WatchStockItem> stocks = await _kiwoomConditionService.GetConditionStocksAsync();
                if (stocks.Count == 0)
                {
                    AppendLog("조건식 결과 0건");
                    ApplyCachedWatchListFallback("조건식 결과 0건");
                    return;
                }

                ApplyWatchList(stocks);
                SaveDailyWatchlistSnapshot(stocks);
                AppendLog($"조건식 결과 {stocks.Count}건 반영");
                ScheduleWatchlistBasePriceRefresh(stocks, TimeSpan.FromSeconds(30));
                _ = StartRealtimeTradeAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"조건식 조회 오류: {ex.Message}");
                if (!ApplyCachedWatchListFallback("조건식 조회 실패"))
                    MessageBox.Show($"키움 조건식(01) 조회 오류: {ex.Message}");
            }
        }

        private bool ApplyCachedWatchListFallback(string reason)
        {
            List<WatchStockItem> cachedStocks = BuildWatchListFromCache();
            if (cachedStocks.Count == 0)
            {
                AppendLog($"{reason}, 사용 가능한 관심종목 캐시 없음");
                return false;
            }

            ApplyWatchList(cachedStocks);
            AppendLog($"{reason}, 관심종목 캐시 {cachedStocks.Count}건 반영");
            ScheduleWatchlistBasePriceRefresh(cachedStocks, TimeSpan.FromSeconds(30));
            _ = StartRealtimeTradeAsync();
            return true;
        }

        private List<WatchStockItem> BuildWatchListFromCache()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            List<WatchlistStockCacheEntry> entries = _watchlistMemoryCache.Values
                .Where(e => !string.IsNullOrWhiteSpace(e.Code) && (e.SnapshotDate == today || e.LastSeenConditionDate == today))
                .OrderBy(e => e.LastUsedAt)
                .ToList();

            if (entries.Count == 0)
            {
                entries = _watchlistMemoryCache.Values
                    .Where(e => !string.IsNullOrWhiteSpace(e.Code))
                    .OrderBy(e => e.LastUsedAt)
                    .Take(10)
                    .ToList();
            }

            return entries
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
                    SupportsNxt = e.SupportsNxt
                })
                .ToList();
        }

        private void ApplyWatchList(IEnumerable<WatchStockItem> stocks)
        {
            var cleanStocks = stocks
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Code))
                .GroupBy(stock => stock.Code ?? string.Empty, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            if (cleanStocks.Count == 0)
                return;

            _watchStocks.Clear();
            _watchStockByCode.Clear();

            foreach (WatchStockItem stock in cleanStocks)
            {
                if (string.IsNullOrWhiteSpace(stock.Name))
                    stock.Name = stock.Code;
                if (string.IsNullOrWhiteSpace(stock.ChangeRateText))
                    stock.ChangeRateText = "-";
                if (string.IsNullOrWhiteSpace(stock.VolumeText))
                    stock.VolumeText = "-";
                stock.PriceBrush = stock.ChangeAmount > 0 ? _upColorBrush : stock.ChangeAmount < 0 ? _downColorBrush : _whiteBrush;
                ApplyWatchlistCacheToStock(stock);
                _watchStocks.Add(stock);
                if (!string.IsNullOrWhiteSpace(stock.Code))
                    _watchStockByCode[stock.Code] = stock;
            }

            AppendLog("왼쪽 목록 갱신");
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
                AppendLog($"관심종목 캐시 로드: {_watchlistMemoryCache.Count}건");
            }
            catch (Exception ex)
            {
                AppendLog($"관심종목 캐시 로드 오류: {ex.Message}");
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

                    entry.SnapshotDate = today;
                    entry.LastSeenConditionDate = today;
                    entry.Market = entry.SupportsNxt ? "KRX,NXT" : "KRX";
                    snapshot.Add(entry);
                }

                _watchlistCacheStore.Save(snapshot);
                AppendLog($"관심종목 캐시 저장: {snapshot.Count}건");
            }
            catch (Exception ex)
            {
                AppendLog($"관심종목 캐시 저장 오류: {ex.Message}");
            }
        }

        private void ScheduleWatchlistBasePriceRefresh(IEnumerable<WatchStockItem> stocks, TimeSpan idleDelay)
        {
            _watchlistCacheRefreshCts?.Cancel();
            _watchlistCacheRefreshCts = new CancellationTokenSource();
            CancellationToken token = _watchlistCacheRefreshCts.Token;
            List<WatchStockItem> snapshot = stocks
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
                })
                .ToList();

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

            try
            {
                _watchlistCacheStore.Save(snapshotCodes
                    .Where(code => _watchlistMemoryCache.ContainsKey(code))
                    .Select(code => _watchlistMemoryCache[code]));
                Dispatcher.Invoke(() => AppendLog("관심종목 기준가 캐시 갱신"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"관심종목 기준가 캐시 저장 오류: {ex.Message}"));
            }
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
        }

        private WatchlistStockCacheEntry? GetWatchlistMemoryCache(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || !_watchlistMemoryCache.TryGetValue(code, out WatchlistStockCacheEntry? entry))
                return null;

            entry.LastUsedAt = DateTime.Now;
            return entry;
        }

        private static bool IsTrustedKrxBasePrice(WatchlistStockCacheEntry? entry, string date)
        {
            // 기준가 변경금지: 화면 기준가는 반드시 KRX 전일종가만 사용한다.
            // NXT 종가, 현재가, LastPrice, 출처 없는 캐시값은 기준가를 덮을 수 없다.
            return entry != null &&
                entry.BasePrice > 0 &&
                string.Equals(entry.BasePriceDate, date, StringComparison.Ordinal) &&
                string.Equals(entry.BasePriceSource, KrxPreviousCloseBasePriceSource, StringComparison.Ordinal);
        }

        private static void SetCachedKrxBasePrice(WatchlistStockCacheEntry entry, long basePrice, string date)
        {
            // KRX 전일종가만 이 함수를 통해 기준가로 확정한다.
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
            await LoadNewsForSelectedStockAsync();
        }

        private async Task LoadNewsForSelectedStockAsync()
        {
            string stockName = WatchListBox.SelectedItem switch
            {
                WatchStockItem stock => stock.Name,
                ListBoxItem listBoxItem => listBoxItem.Tag as string ?? string.Empty,
                _ => string.Empty
            };
            string stockCode = WatchListBox.SelectedItem is WatchStockItem selectedWatch ? selectedWatch.Code : string.Empty;
            if (string.IsNullOrWhiteSpace(stockName))
                stockName = stockCode;
            if (string.IsNullOrWhiteSpace(stockName))
                return;

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
            _recentTrades.Clear();
            _buyTradeVolume = 0;
            _sellTradeVolume = 0;
            _selectedPreviousVolume = 0;
            _krxPrevClosePrice = 0;
            _lastTickPriceByCode.Clear();
            _currentChartCandles.Clear();
            _currentChartCode = string.Empty;
            _lastRealtimeChartDrawAt = DateTime.MinValue;
            ClearSelectedChartVisuals();
            HogaStatusText.Text = "현재가 - / 등락률 - / 0D -";
            UpdateHogaSummary(0, 0);
            ResetTradeSummaryInfo();
            InfoBasePriceText.Text = "-";
            InfoBasePriceText.Foreground = _whiteBrush;
            AppendLog($"종목 선택: {stockName}");

            _ = LoadNewsAsync(stockName, selectionVersion);
            _ = LoadDisclosuresAsync(stockCode, selectionVersion, requestToken);
            await LoadSelectedBasePriceAsync(stockCode, selectionVersion, requestToken);
            if (!IsCurrentSelection(stockCode, selectionVersion))
                return;

            await LoadSelectedOrderBookSnapshotAsync(stockCode, selectionVersion, requestToken);
            if (!IsCurrentSelection(stockCode, selectionVersion))
                return;

            _ = RegisterSelectedRealtime0DIfReadyAsync();
            StartSelectedChartRender();
            _ = LoadSelectedStockStatusAsync(stockCode, selectionVersion, requestToken);
            _ = LoadKrxClosingSnapshotIfNeededAsync(stockCode, selectionVersion, requestToken);
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
                // 기준가 변경금지: 검증된 KRX 전일종가 캐시가 없을 때만 KRX 기준가를 조회한다.
                // 다른 시장/실시간/종목정보 값으로 대체하지 않는다.
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
                    ? $"기준가 잠금(KRX 전일종가{(IsTrustedKrxBasePrice(cache, today) ? " 캐시" : "")}): {basePrice:N0}"
                    : "기준가 잠금 실패(KRX 전일종가, NXT 값 대체 금지)");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"기준가 선조회 오류: {ex.Message}");
            }
        }

        private async Task LoadNewsAsync(string stockName, int selectionVersion)
        {
            try
            {
                var news = await _newsService.GetLatestNewsAsync(stockName, _config.Dashboard.NewsCount);
                if (selectionVersion != _selectionVersion)
                    return;

                StockNewsListBox.ItemsSource = news;
                AppendLog($"뉴스 로드: {stockName} ({news.Count}건)");
            }
            catch (Exception ex)
            {
                AppendLog($"뉴스 조회 오류: {ex.Message}");
                MessageBox.Show($"뉴스 조회 오류: {ex.Message}");
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
                AppendLog($"공시 로드: {stockCode} ({disclosures.Count}건)");
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
                AppendLog($"공시 조회 오류: {ex.GetType().Name} / {ex.Message}");
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
                    AppendLog($"NXT 호가 조회값 없음, KRX로 재조회: {stockCode}");
                    snapshot = await _kiwoomConditionService.GetOrderBookSnapshotAsync(stockCode, false, cancellationToken);
                    useNxtMarket = false;
                    if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                        return;
                }

                if (!HasAnyHogaLevel(snapshot))
                {
                    if (TryApplyCurrentPriceFallbackHoga(stockCode, "호가 REST fallback"))
                        return;

                    AppendLog($"호가 REST 조회값 없음, 기존 호가 유지: {stockCode}");
                    return;
                }

                ApplyHogaRows(
                    snapshot.SellLevels.Select(r => (r.Price, r.Quantity)).ToList(),
                    snapshot.BuyLevels.Select(r => (r.Price, r.Quantity)).ToList(),
                    useNxtMarket ? "NXT 호가 REST" : "KRX 호가 REST");
                AppendLog($"{(useNxtMarket ? "NXT" : "KRX")} 호가 REST 적용: {stockCode}");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"호가 REST 조회 오류: {ex.Message}");
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

                bool useNxtMarket = ShouldUseNxtDataForStock(stockCode);
                StockStatusMetrics m = await _kiwoomConditionService.GetStockStatusMetricsByGuideAsync(stockCode, useNxtMarket, cancellationToken);
                if (useNxtMarket && IsEmptyStockStatus(m) && !IsNxtFrozenWindow())
                {
                    AppendLog($"NXT 조회값 없음, KRX로 재조회: {stockCode}");
                    useNxtMarket = false;
                    m = await _kiwoomConditionService.GetStockStatusMetricsByGuideAsync(stockCode, false, cancellationToken);
                }

                if (IsEmptyStockStatus(m))
                {
                    AppendLog($"종목정보 조회값 없음, 기존 정보 유지: {stockCode}");
                    return;
                }

                _currentStatusMetrics = m;
                ApplySelectedWatchStockPriceInfo(stockCode, m);
                (string dailyVolumeRatioText, Brush dailyVolumeRatioBrush) = await GetDailyVolumeRatioAsync(stockCode, useNxtMarket, m, cancellationToken);
                if (selectionVersion != _selectionVersion)
                    return;

                m.VolumeRatioText = dailyVolumeRatioText;
                AppendLog(_krxPrevClosePrice > 0
                    ? $"호가 기준가(KRX 전일종가): {_krxPrevClosePrice:N0}"
                    : "호가 기준가(KRX 전일종가) 조회 실패");
                SetSelectedStockSubInfo(m, dailyVolumeRatioText, dailyVolumeRatioBrush);
                InfoOpenPriceText.Text = m.OpenPriceText;
                InfoHighPriceText.Text = m.HighPriceText;
                InfoLowPriceText.Text = m.LowPriceText;
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
                    AppendLog($"체결량 조회값 없음 또는 총량 불일치, 체결량 표시 제외: {stockCode}");
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
                    InfoPrevTimeVolumeRatioText.Text = verifiedVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Foreground = verifiedVolumeRatioBrush;
                    AppendLog($"일별거래상세 통합 합계: 총 {verifiedVolume:N0} / 장전 {exec.BeforeMarketTradeQty:N0} / 장중 {exec.RegularMarketTradeQty:N0} / 장후 {exec.AfterMarketTradeQty:N0}");
                }
                else if (exec.DailyTradeQty > 0 || exec.DailySectionTradeQty > 0)
                {
                    AppendLog($"장중 거래량비율은 현재 누적거래량 기준 유지: {stockCode}");
                }
                ApplyProgramTradeInfo(exec);
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"상태표시줄 조회 오류: {ex.Message}");
            }
        }

        private async Task<(string Text, Brush Brush)> GetDailyVolumeRatioAsync(string stockCode, bool useNxtMarket, StockStatusMetrics metrics, CancellationToken cancellationToken = default)
        {
            long todayVolume = ParseLongAbs(metrics.VolumeText);
            if (todayVolume <= 0)
                return ("-", _whiteBrush);

            IReadOnlyList<(string Date, long Volume)> volumes = await _kiwoomConditionService.GetUnifiedDailyTradeVolumesAsync(stockCode, 5, cancellationToken);
            List<(string Date, long Volume)> orderedVolumes = volumes
                .Where(v => !string.IsNullOrWhiteSpace(v.Date) && v.Volume > 0)
                .OrderByDescending(v => v.Date)
                .ToList();

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

        private async Task<StockStatusMetrics> LoadExecutionSummaryByMarketAsync(string stockCode, bool useNxtMarketNow, CancellationToken cancellationToken)
        {
            bool supportsNxt = _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected) && selected.SupportsNxt;
            string programMarketType = selected?.ProgramMarketType ?? string.Empty;
            bool useNxtExecution = supportsNxt && (useNxtMarketNow || IsNxtFrozenWindow());
            StockStatusMetrics exec = await _kiwoomConditionService.GetTodayExecutionSummaryAsync(stockCode, useNxtExecution, programMarketType, cancellationToken);
            if (!useNxtExecution || HasAnyExecutionSummaryValue(exec) || IsNxtFrozenWindow())
                return exec;

            AppendLog($"NXT 체결량 조회값 없음, KRX로 재조회: {stockCode}");
            StockStatusMetrics krxExec = await _kiwoomConditionService.GetTodayExecutionSummaryAsync(stockCode, false, programMarketType, cancellationToken);
            if (!krxExec.HasProgramTrade && exec.HasProgramTrade)
            {
                krxExec.ProgramBuyText = exec.ProgramBuyText;
                krxExec.ProgramNetQuantity = exec.ProgramNetQuantity;
                krxExec.HasProgramTrade = true;
            }
            if (krxExec.DailyTradeQty <= 0 && exec.DailyTradeQty > 0)
                krxExec.DailyTradeQty = exec.DailyTradeQty;
            if (krxExec.BeforeMarketTradeQty <= 0 && exec.BeforeMarketTradeQty > 0)
                krxExec.BeforeMarketTradeQty = exec.BeforeMarketTradeQty;
            if (krxExec.RegularMarketTradeQty <= 0 && exec.RegularMarketTradeQty > 0)
                krxExec.RegularMarketTradeQty = exec.RegularMarketTradeQty;
            if (krxExec.AfterMarketTradeQty <= 0 && exec.AfterMarketTradeQty > 0)
                krxExec.AfterMarketTradeQty = exec.AfterMarketTradeQty;
            if (krxExec.DailySectionTradeQty <= 0 && exec.DailySectionTradeQty > 0)
                krxExec.DailySectionTradeQty = exec.DailySectionTradeQty;
            return krxExec;
        }

        private bool ShouldUseFinalDailyVolumeForRatio(string stockCode, bool useNxtMarket)
        {
            bool supportsNxt = IsNxtSupportedStock(stockCode);
            if (supportsNxt && useNxtMarket)
                return IsNxtFrozenWindow();

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
                SelectedStockSubInfo.Inlines.Add(new Run($"전일종가 {(_krxPrevClosePrice > 0 ? _krxPrevClosePrice.ToString("N0") : "-")} · "));
            }

            SelectedStockSubInfo.Inlines.Add(new Run(
                $"시가총액 {metrics.MarketCapText} · 유통주식수 {metrics.ListedSharesText}"));
        }

        private async Task LoadKrxClosingSnapshotIfNeededAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                    return;
                if (selectionVersion != _selectionVersion)
                    return;

                bool useNxtSnapshot = _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected)
                    && selected.SupportsNxt
                    && IsNxtFrozenWindow();

                if (!IsKrxRegularClosedWindow() && !useNxtSnapshot)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                string cacheKey = BuildClosingSnapshotCacheKey(stockCode, useNxtSnapshot);
                if (_closingSnapshotMemoryCache.TryGetValue(cacheKey, out ClosingSnapshotCacheEntry? cached))
                {
                    ApplyClosingSnapshot(CloneClosingSnapshot(cached.Snapshot), cached.IsNxtSnapshot ? "NXT 20시 최종 캐시" : "KRX 종가 캐시");
                    AppendLog($"{(cached.IsNxtSnapshot ? "NXT 20시 최종" : "KRX 종가")} 스냅샷 캐시 적용: {stockCode}");
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
                ApplyClosingSnapshot(snapshot, useNxtSnapshot ? "NXT 20시 최종" : "KRX 종가");
                AppendLog($"{(useNxtSnapshot ? "NXT 20시 최종" : "KRX 종가")} 스냅샷 적용: {stockCode}");
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"장마감 스냅샷 오류: {ex.Message}");
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
                ApplyHogaRows(snapshot.SellLevels.Select(r => (r.Price, r.Quantity)).ToList(), snapshot.BuyLevels.Select(r => (r.Price, r.Quantity)).ToList(), $"{source} 스냅샷");
            else
                AppendLog($"{source} 스냅샷 호가 없음, 기존 호가 유지: {snapshot.Code}");
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
                AppendLog($"{source} 일별거래상세 통합 합계: 총 {verifiedVolume:N0} / 장전 {snapshot.BeforeMarketTradeQty:N0} / 장중 {snapshot.RegularMarketTradeQty:N0} / 장후 {snapshot.AfterMarketTradeQty:N0}");
            }

            ResetTradeSummaryInfo();
            AppendLog($"{source} 장마감 후 매수/매도체결량 공란 처리: {snapshot.Code}");

            if (snapshot.RecentTrades.Count == 0)
            {
                AppendLog($"{source} 최근체결 없음, 기존 체결목록 유지: {snapshot.Code}");
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
            HogaStatusText.Text = $"현재가 {(currentPrice > 0 ? currentPrice.ToString("N0") : stock.CurrentPrice > 0 ? stock.CurrentPrice.ToString("N0") : "-")} / 등락률 {rateText} / 기준가 {(basePrice > 0 ? basePrice.ToString("N0") : "-")}";
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logLines.Enqueue(line);

            while (_logLines.Count > MaxLogLines)
            {
                _logLines.Dequeue();
            }

            LeftLogTextBox.Text = string.Join(Environment.NewLine, _logLines);
            LeftLogTextBox.ScrollToEnd();
        }

        private void SetStartupLoading(bool isVisible, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStartupLoading(isVisible, message));
                return;
            }

            StartupLoadingOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            StartupLoadingText.Text = string.IsNullOrWhiteSpace(message) ? "준비 중..." : message;
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
                text = text.Substring(1);

            string digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return string.Empty;

            return digits.Length >= 6 ? digits.Substring(digits.Length - 6) : digits.PadLeft(6, '0');
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



    }
}


