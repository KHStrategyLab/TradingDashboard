using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private ChartPeriod _currentChartPeriod = ChartPeriod.Daily;
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
        private DateTime _lastMarketStatusAt = DateTime.MinValue;
        private DateTime _marketStatusUnknownUntil = DateTime.MinValue;
        private bool _isNxtMarketMode;
        private ClientWebSocket? _realtimeWs;
        private CancellationTokenSource? _realtimeCts;
        private CancellationTokenSource? _selectedRequestCts;
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
            await PrimeMarketStatusBeforeWatchlistAsync();
            await LoadWatchListFromKiwoomConditionAsync();

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
                MessageBox.Show($"키움 조건식(01) 조회 오류: {ex.Message}");
            }
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
                        stock.LastPrice,
                        stock.OrderWarning,
                        stock.AuditInfo,
                        stock.StockState,
                        stock.SectorName,
                        null,
                        null,
                        saveFile: false);
                    if (entry.BasePrice <= 0 && stock.LastPrice > 0)
                    {
                        entry.BasePrice = stock.LastPrice;
                        entry.BasePriceDate = today;
                    }

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
                if (entry.BasePrice > 0 && entry.BasePriceDate == today)
                    continue;

                try
                {
                    long basePrice = await _kiwoomConditionService.GetKrxPreviousClosePriceAsync(code, cancellationToken);
                    if (basePrice <= 0)
                        continue;

                    entry.BasePrice = basePrice;
                    entry.BasePriceDate = today;
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

        private WatchlistStockCacheEntry UpsertWatchlistMemoryCache(string code, string name, bool? supportsNxt, string? marketTypeCode, string? marketName, string? programMarketType, long? lastPrice, string? orderWarning, string? auditInfo, string? stockState, string? sectorName, long? basePrice, string? basePriceDate, bool saveFile)
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
            if (basePrice.HasValue && basePrice.Value > 0)
                entry.BasePrice = basePrice.Value;
            if (!string.IsNullOrWhiteSpace(basePriceDate))
                entry.BasePriceDate = basePriceDate;
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
            _selectedRequestCts?.Cancel();
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
            HogaStatusText.Text = "현재가 - / 등락률 - / 0D -";
            UpdateHogaSummary(0, 0);
            ResetTradeSummaryInfo();
            InfoBasePriceText.Text = "-";
            InfoBasePriceText.Foreground = _whiteBrush;
            AppendLog($"종목 선택: {stockName}");

            _ = LoadNewsAsync(stockName, selectionVersion);
            _ = LoadDisclosuresAsync(stockCode, selectionVersion, requestToken);
            await LoadSelectedBasePriceAsync(stockCode, selectionVersion, requestToken);
            await LoadSelectedOrderBookSnapshotAsync(stockCode, selectionVersion, requestToken);
            _ = RegisterSelectedRealtime0DIfReadyAsync();
            await RenderSelectedChartAsync(selectionVersion, stockCode, requestToken);
            _ = LoadSelectedStockStatusAsync(stockCode, selectionVersion, requestToken);
            _ = LoadKrxClosingSnapshotIfNeededAsync(stockCode, selectionVersion, requestToken);
        }

        private async Task LoadSelectedBasePriceAsync(string stockCode, int selectionVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                if (selectionVersion != _selectionVersion || !_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                    return;

                string today = DateTime.Now.ToString("yyyyMMdd");
                WatchlistStockCacheEntry? cache = GetWatchlistMemoryCache(stockCode);
                long basePrice = cache != null && cache.BasePrice > 0 && cache.BasePriceDate == today
                    ? cache.BasePrice
                    : await _kiwoomConditionService.GetKrxPreviousClosePriceAsync(stockCode, cancellationToken);
                if (selectionVersion != _selectionVersion)
                    return;

                if (basePrice > 0)
                    UpsertWatchlistMemoryCache(
                        stockCode,
                        _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? stock) ? stock.Name : stockCode,
                        null,
                        stock?.MarketTypeCode,
                        stock?.MarketName,
                        stock?.ProgramMarketType,
                        stock?.LastPrice,
                        stock?.OrderWarning,
                        stock?.AuditInfo,
                        stock?.StockState,
                        stock?.SectorName,
                        basePrice,
                        today,
                        saveFile: false);
                _krxPrevClosePrice = basePrice;
                InfoBasePriceText.Text = basePrice > 0 ? basePrice.ToString("N0") : "-";
                InfoBasePriceText.Foreground = _whiteBrush;
                AppendLog(basePrice > 0
                    ? $"기준가 선조회(ka10007 전일종가): {basePrice:N0}"
                    : "기준가 선조회 실패(ka10007 전일종가)");
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
                    m.TurnoverRateText = FormatTurnoverRate(verifiedVolume, ParseLongAbs(m.ListedSharesText));
                    InfoTurnoverRateText.Text = m.TurnoverRateText;
                    (string verifiedVolumeRatioText, Brush verifiedVolumeRatioBrush) = FormatDailyVolumeRatio(verifiedVolume);
                    m.VolumeRatioText = verifiedVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Text = verifiedVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Foreground = verifiedVolumeRatioBrush;
                    AppendLog($"일별거래상세 합계: 총 {verifiedVolume:N0} / 장전 {exec.BeforeMarketTradeQty:N0} / 장중 {exec.RegularMarketTradeQty:N0} / 장후 {exec.AfterMarketTradeQty:N0}");
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

            List<DailyCandle> candles = await _kiwoomConditionService.GetDailyCandlesAsync(stockCode, useNxtMarket, 5, cancellationToken);
            List<DailyCandle> orderedCandles = candles
                .Where(c => !string.IsNullOrWhiteSpace(c.Date))
                .OrderByDescending(c => c.Date)
                .ToList();

            DailyCandle? latestCandle = orderedCandles.FirstOrDefault();
            DailyCandle? previousTradingDay = ResolvePreviousVolumeCandle(orderedCandles);

            long previousVolume = previousTradingDay?.Volume ?? 0;
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
                SelectedStockSubInfo.Inlines.Add(new Run($"전일종가 {stock.LastPriceText} · "));
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
                if (!string.IsNullOrWhiteSpace(snapshot.ChangeRateText) && snapshot.ChangeRateText != "-")
                    stock.ChangeRateText = snapshot.ChangeRateText;
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
                (string verifiedVolumeRatioText, Brush verifiedVolumeRatioBrush) = FormatDailyVolumeRatio(verifiedVolume);
                InfoPrevTimeVolumeRatioText.Text = verifiedVolumeRatioText;
                InfoPrevTimeVolumeRatioText.Foreground = verifiedVolumeRatioBrush;
                _currentStatusMetrics.VolumeRatioText = verifiedVolumeRatioText;
                if (_watchStockByCode.TryGetValue(snapshot.Code, out WatchStockItem? stockForVolume))
                    stockForVolume.VolumeText = InfoVolumeText.Text;
                AppendLog($"{source} 일별거래상세 합계: 총 {verifiedVolume:N0} / 장전 {snapshot.BeforeMarketTradeQty:N0} / 장중 {snapshot.RegularMarketTradeQty:N0} / 장후 {snapshot.AfterMarketTradeQty:N0}");
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
            if (!string.IsNullOrWhiteSpace(metrics.ChangeRateText) && metrics.ChangeRateText != "-")
                stock.ChangeRateText = metrics.ChangeRateText;
            stock.PriceBrush = currentPrice > 0
                ? ResolveHogaBrushByKrxPrevClose(currentPrice)
                : changeAmount > 0 ? _upColorBrush : changeAmount < 0 ? _downColorBrush : _whiteBrush;

            string rateText = !string.IsNullOrWhiteSpace(metrics.ChangeRateText) && metrics.ChangeRateText != "-"
                ? metrics.ChangeRateText
                : stock.ChangeRateText;
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

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _selectedRequestCts?.Cancel();
                _selectedRequestCts?.Dispose();
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

        private async Task StartRealtimeTradeAsync()
        {
            try
            {
                if (_watchStocks.Count == 0 || !_config.Kiwoom.UseRestApi)
                    return;

                _realtimeCts?.Cancel();
                _realtimeWs?.Dispose();
                _realtimeCts = new CancellationTokenSource();
                CancellationToken ct = _realtimeCts.Token;

                string token = await _kiwoomConditionService.GetAccessTokenAsync(ct);

                _realtimeWs = new ClientWebSocket();
                await _realtimeWs.ConnectAsync(new Uri("wss://api.kiwoom.com:10000/api/dostk/websocket"), ct);
                AppendLog("0B WS 연결");

                await SendWsJsonAsync(_realtimeWs, new { trnm = "LOGIN", token }, ct);
                using JsonDocument login = await ReceiveByTrNameAsync(_realtimeWs, "LOGIN", ct);
                string loginCode = ReadString(login.RootElement, "return_code");
                if (loginCode != "0")
                {
                    AppendLog($"0B LOGIN 실패: {loginCode}");
                    return;
                }

                await RegisterMarketStatusAsync(_realtimeWs, ct);
                await RegisterRealtime0BAsync(_realtimeWs, ct);
                await RegisterSelectedRealtime0DAsync(_realtimeWs, ct);
                AppendLog($"0B 등록 완료: {_watchStockByCode.Count}종목");

                _ = Task.Run(() => ReceiveRealtimeLoopAsync(_realtimeWs, ct), ct);
            }
            catch (Exception ex)
            {
                AppendLog($"0B 시작 오류: {ex.Message}");
            }
        }

        private async Task RegisterMarketStatusAsync(ClientWebSocket ws, CancellationToken ct)
        {
            await SendWsJsonAsync(ws, new
            {
                trnm = "REG",
                grp_no = "899",
                refresh = "1",
                data = new[]
                {
                    new
                    {
                        item = new[] { string.Empty },
                        type = MarketStatusTypes
                    }
                }
            }, ct);

            AppendLog("0s 장운영구분 등록");
        }

        private async Task RegisterRealtime0BAsync(ClientWebSocket ws, CancellationToken ct)
        {
            string[] krxItems = _watchStockByCode.Keys
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (krxItems.Length == 0)
                return;

            await SendWsJsonAsync(ws, new
            {
                trnm = "REG",
                grp_no = "900",
                refresh = "1",
                data = new[]
                {
                    new
                    {
                        item = krxItems,
                        type = new[] { "0B" }
                    }
                }
            }, ct);

            if (!ShouldUseNxtMarketNow())
                return;

            string[] nxtItems = _watchStockByCode
                .Where(kv => kv.Value.SupportsNxt)
                .Select(kv => $"{kv.Key}_NX")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (nxtItems.Length == 0)
                return;

            await SendWsJsonAsync(ws, new
            {
                trnm = "REG",
                grp_no = "900",
                refresh = "0",
                data = new[]
                {
                    new
                    {
                        item = nxtItems,
                        type = new[] { "0B" }
                    }
                }
            }, ct);

            AppendLog($"0B NXT 장후 등록: {nxtItems.Length}종목");
        }

        private static bool IsNxtMarketWindow()
        {
            TimeSpan now = DateTime.Now.TimeOfDay;
            return (now >= new TimeSpan(8, 0, 0) && now < new TimeSpan(9, 0, 0)) ||
                   (now >= new TimeSpan(15, 40, 0) && now < new TimeSpan(20, 0, 0));
        }

        private bool ShouldUseNxtMarketNow()
        {
            DateTime now = DateTime.Now;
            if (now < _marketStatusUnknownUntil)
                return false;

            if ((now - _lastMarketStatusAt).TotalSeconds <= 180)
            {
                if (IsNxtOpenStatus(_lastMarketStatusCode))
                    return true;

                if (IsNxtClosedStatus(_lastMarketStatusCode))
                    return false;
            }

            bool isNxtTime = IsNxtMarketWindow();
            if (!isNxtTime)
                return false;

            return true;
        }

        private static bool IsNxtOpenStatus(string code)
        {
            return string.Equals(code, "P", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "R", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "T", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "U", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNxtClosedStatus(string code)
        {
            return string.Equals(code, "Q", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "S", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "V", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeMarketStatus(string code)
        {
            return code switch
            {
                "0" => "KRX 장시작전 알림",
                "3" => "KRX 장시작",
                "2" => "KRX 장마감 알림",
                "4" => "KRX 장마감",
                "8" => "KRX 정규장마감",
                "9" => "전체장마감",
                "a" => "KRX 시간외 종가매매 시작",
                "b" => "KRX 시간외 종가매매 종료",
                "c" => "KRX 시간외 단일가 시작",
                "d" => "KRX 시간외 단일가 종료",
                "e" => "선옵 장마감전 동시호가 종료",
                "f" => "선물옵션 장운영시간 알림",
                "o" => "선옵 장시작",
                "s" => "선옵 장마감전 동시호가 시작",
                "P" => "NXT 프리마켓 시작",
                "Q" => "NXT 프리마켓 종료",
                "R" => "NXT 메인마켓 시작",
                "S" => "NXT 메인마켓 종료",
                "T" => "NXT 에프터마켓 단일가 시작",
                "U" => "NXT 에프터마켓 시작",
                "V" => "NXT 에프터마켓 종료",
                _ => string.IsNullOrWhiteSpace(code) ? "장상태 미확인" : $"장상태 미정의({code})"
            };
        }

        private bool ShouldUseNxtDataForStock(string stockCode)
        {
            return IsNxtSupportedStock(stockCode)
                && (ShouldUseNxtMarketNow() || IsNxtFrozenWindow());
        }

        private bool IsNxtSupportedStock(string stockCode)
        {
            return _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected)
                && selected.SupportsNxt;
        }

        private static bool IsKrxRegularClosedWindow()
        {
            TimeSpan now = DateTime.Now.TimeOfDay;
            return now >= new TimeSpan(15, 30, 0) || now < new TimeSpan(7, 0, 0);
        }

        private static bool IsNxtFrozenWindow()
        {
            TimeSpan now = DateTime.Now.TimeOfDay;
            return now >= new TimeSpan(20, 0, 0) || now < new TimeSpan(7, 0, 0);
        }

        private async Task ReceiveRealtimeLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string text = await ReceiveTextAsync(ws, ct);
                    using JsonDocument doc = JsonDocument.Parse(text);
                    JsonElement root = doc.RootElement;
                    string trnm = ReadString(root, "trnm");

                    if (trnm == "PING")
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(text);
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                        continue;
                    }

                    if (trnm == "REAL" || trnm == "0B" || trnm == "0D" || trnm == "0H")
                    {
                        ApplyRealtimePayload(root);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"0B 수신 오류: {ex.Message}"));
            }
        }

        private void ApplyRealtimePayload(JsonElement root)
        {
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            {
                ApplyRealtimeItem(root);
                return;
            }

            foreach (JsonElement item in data.EnumerateArray())
            {
                string type = ReadString(item, "type");
                if (string.Equals(type, "0s", StringComparison.OrdinalIgnoreCase))
                    ApplyMarketStatusItem(item);
                else if (string.Equals(type, "0D", StringComparison.OrdinalIgnoreCase))
                    ApplyRealtimeHogaItem(item);
                else if (string.Equals(type, "0H", StringComparison.OrdinalIgnoreCase))
                    ApplyRealtimeExpectedItem(item);
                else
                    ApplyRealtimeItem(item);
            }
        }

        private void ApplyMarketStatusItem(JsonElement item)
        {
            MarketStatusSnapshot status = ExtractMarketStatusSnapshot(item);
            if (string.IsNullOrWhiteSpace(status.Code))
                return;

            ApplyMarketStatusSnapshot(status, allowRefresh: true);
        }

        private void ApplyMarketStatusSnapshot(MarketStatusSnapshot status, bool allowRefresh)
        {
            bool previousMode = _isNxtMarketMode;
            string previousCode = _lastMarketStatusCode;
            _lastMarketStatusCode = status.Code.Trim();
            _lastMarketStatusTime = status.Time;
            _lastMarketExpectedRemain = status.ExpectedRemain;
            _lastMarketStatusText = DescribeMarketStatus(_lastMarketStatusCode);
            _lastMarketStatusAt = DateTime.Now;
            _marketStatusUnknownUntil = DateTime.MinValue;
            _isNxtMarketMode = ShouldUseNxtMarketNow();

            if (!allowRefresh)
                return;

            if (previousMode == _isNxtMarketMode && string.Equals(previousCode, _lastMarketStatusCode, StringComparison.OrdinalIgnoreCase))
                return;

            Dispatcher.Invoke(() => AppendLog($"0s 장운영구분: 215={_lastMarketStatusCode} / {_lastMarketStatusText} / {(_isNxtMarketMode ? "NXT 사용" : "KRX 사용")}"));
            _ = RefreshRealtimeRegistrationAfterMarketStatusAsync();
        }

        private async Task RefreshRealtimeRegistrationAfterMarketStatusAsync()
        {
            try
            {
                if (_realtimeWs == null || _realtimeWs.State != WebSocketState.Open || _realtimeCts == null)
                    return;

                await RegisterRealtime0BAsync(_realtimeWs, _realtimeCts.Token);
                await RegisterSelectedRealtime0DAsync(_realtimeWs, _realtimeCts.Token);

                string selectedCode = _selectedStockCode;
                int selectionVersion = _selectionVersion;
                if (!string.IsNullOrWhiteSpace(selectedCode))
                {
                    await LoadSelectedOrderBookSnapshotAsync(selectedCode, selectionVersion);
                    _ = RenderSelectedChartAsync(selectionVersion, selectedCode);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"0s 반영 재등록 오류: {ex.Message}"));
            }
        }

        private void ApplyRealtimeHogaItem(JsonElement item)
        {
            string rawCode = ReadAnyRealtime(item, "item", "stk_cd", "stkCd", "code", "jm_code", "9001");
            string code = NormalizeStockCode(rawCode);
            if (string.IsNullOrWhiteSpace(code) || code != _selectedStockCode)
                return;

            JsonElement values = item;
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("values", out JsonElement nestedValues))
                values = nestedValues;

            Dispatcher.Invoke(() =>
            {
                var sellRows = new List<(long Price, long Qty)>(10);
                var buyRows = new List<(long Price, long Qty)>(10);

                for (int i = 0; i < 10; i++)
                {
                    int level = i + 1;
                    long sellPriceNum = ParseLongAbs(ReadAnyRealtime(values, SellPriceKeys[i], level == 1 ? "sel_fpr_bid" : string.Empty, $"sel_{level}bid", $"sel_{level}_bid", $"sel_{level}th_pre_bid", $"sell_{level}_price"));
                    long sellQtyNum = ParseLongAbs(ReadAnyRealtime(values, SellQtyKeys[i], level == 1 ? "sel_fpr_req" : string.Empty, $"sel_{level}bid_req", $"sel_{level}_req", $"sel_{level}th_pre_req", $"sell_{level}_qty"));
                    sellRows.Add((sellPriceNum, sellQtyNum));

                    long buyPriceNum = ParseLongAbs(ReadAnyRealtime(values, BuyPriceKeys[i], level == 1 ? "buy_fpr_bid" : string.Empty, $"buy_{level}bid", $"buy_{level}_bid", $"buy_{level}th_pre_bid", $"buy_{level}_price"));
                    long buyQtyNum = ParseLongAbs(ReadAnyRealtime(values, BuyQtyKeys[i], level == 1 ? "buy_fpr_req" : string.Empty, $"buy_{level}bid_req", $"buy_{level}_req", $"buy_{level}th_pre_req", $"buy_{level}_qty"));
                    buyRows.Add((buyPriceNum, buyQtyNum));
                }

                var sellDisplayRows = sellRows.AsEnumerable().Reverse().Take(10).ToList();
                var buyDisplayRows = buyRows.Take(10).ToList();

                if (!sellDisplayRows.Any(r => r.Price > 0 || r.Qty > 0) && !buyDisplayRows.Any(r => r.Price > 0 || r.Qty > 0))
                {
                    if (TryApplyCurrentPriceFallbackHoga(code, "0D fallback"))
                        return;

                    AppendLog($"0D 빈 호가 수신, 기존 호가 유지: {code}");
                    return;
                }

                for (int i = 0; i < 10; i++)
                {
                    _sellHogaLevels[i].PriceText = "-";
                    _sellHogaLevels[i].QtyText = "-";
                    _sellHogaLevels[i].RateText = string.Empty;
                    _sellHogaLevels[i].RawPrice = 0;
                    _sellHogaLevels[i].PriceBrush = _whiteBrush;
                    _sellHogaLevels[i].RateBrush = _whiteBrush;

                    _buyHogaLevels[i].PriceText = "-";
                    _buyHogaLevels[i].QtyText = "-";
                    _buyHogaLevels[i].RateText = string.Empty;
                    _buyHogaLevels[i].RawPrice = 0;
                    _buyHogaLevels[i].PriceBrush = _whiteBrush;
                    _buyHogaLevels[i].RateBrush = _whiteBrush;
                }

                for (int i = 0; i < sellDisplayRows.Count && i < 10; i++)
                {
                    long price = sellDisplayRows[i].Price;
                    long qty = sellDisplayRows[i].Qty;
                    _sellHogaLevels[i].PriceText = price > 0 ? price.ToString("N0") : "-";
                    _sellHogaLevels[i].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                    _sellHogaLevels[i].RawPrice = price;
                    _sellHogaLevels[i].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
                }

                for (int i = 0; i < buyDisplayRows.Count && i < 10; i++)
                {
                    long price = buyDisplayRows[i].Price;
                    long qty = buyDisplayRows[i].Qty;
                    _buyHogaLevels[i].PriceText = price > 0 ? price.ToString("N0") : "-";
                    _buyHogaLevels[i].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                    _buyHogaLevels[i].RawPrice = price;
                    _buyHogaLevels[i].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
                }

                _last0DReceivedAt = DateTime.Now;
                long totalSell = sellDisplayRows.Sum(r => r.Qty);
                long totalBuy = buyDisplayRows.Sum(r => r.Qty);
                UpdateHogaSummary(totalSell, totalBuy);
                HogaStatusText.Text = $"현재가 {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s) && s.CurrentPrice > 0 ? s.CurrentPrice.ToString("N0") : "-")} / 등락률 {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s2) ? s2.ChangeRateText : "-")} / 0D {_last0DReceivedAt:HH:mm:ss}";

                HighlightCenterPriceInHoga();
            });
        }

        private void ApplyRealtimeExpectedItem(JsonElement item)
        {
            string rawCode = ReadAnyRealtime(item, "item", "stk_cd", "stkCd", "code", "jm_code", "9001");
            string code = NormalizeStockCode(rawCode);
            if (string.IsNullOrWhiteSpace(code) || code != _selectedStockCode)
                return;

            string cur = ReadAnyRealtime(item, "10", "cur_prc", "curPrc", "price", "now_prc");
            string rate = ReadAnyRealtime(item, "12", "flu_rt", "chg_rt", "change_rate");
            if (!string.IsNullOrWhiteSpace(rate) && !rate.Contains("%"))
                rate += "%";
            long curNum = ParseLongAbs(cur);

            Dispatcher.Invoke(() =>
            {
                if (curNum > 0 && _watchStockByCode.TryGetValue(code, out WatchStockItem? stock))
                    stock.CurrentPrice = curNum;

                HogaStatusText.Text = $"현재가 {(curNum > 0 ? curNum.ToString("N0") : "-")} / 등락률 {(string.IsNullOrWhiteSpace(rate) ? "-" : rate)} / 0D {(_last0DReceivedAt == DateTime.MinValue ? "-" : _last0DReceivedAt.ToString("HH:mm:ss"))}";
                HighlightCenterPriceInHoga();
            });
        }

        private void ApplyHogaRows(List<(long Price, long Qty)> sellRows, List<(long Price, long Qty)> buyRows, string source)
        {
            var sellDisplayRows = sellRows.AsEnumerable().Reverse().Take(10).ToList();
            var buyDisplayRows = buyRows.Take(10).ToList();

            if (!sellDisplayRows.Any(r => r.Price > 0 || r.Qty > 0) && !buyDisplayRows.Any(r => r.Price > 0 || r.Qty > 0))
            {
                if (TryApplyCurrentPriceFallbackHoga(_selectedStockCode, $"{source} fallback"))
                    return;

                AppendLog($"{source} 빈 호가, 기존 호가 유지");
                return;
            }

            for (int i = 0; i < 10; i++)
            {
                _sellHogaLevels[i].PriceText = "-";
                _sellHogaLevels[i].QtyText = "-";
                _sellHogaLevels[i].RateText = string.Empty;
                _sellHogaLevels[i].RawPrice = 0;
                _sellHogaLevels[i].PriceBrush = _whiteBrush;
                _sellHogaLevels[i].RateBrush = _whiteBrush;

                _buyHogaLevels[i].PriceText = "-";
                _buyHogaLevels[i].QtyText = "-";
                _buyHogaLevels[i].RateText = string.Empty;
                _buyHogaLevels[i].RawPrice = 0;
                _buyHogaLevels[i].PriceBrush = _whiteBrush;
                _buyHogaLevels[i].RateBrush = _whiteBrush;
            }

            for (int i = 0; i < sellDisplayRows.Count && i < 10; i++)
            {
                long price = sellDisplayRows[i].Price;
                long qty = sellDisplayRows[i].Qty;
                _sellHogaLevels[i].PriceText = price > 0 ? price.ToString("N0") : "-";
                _sellHogaLevels[i].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                _sellHogaLevels[i].RawPrice = price;
                _sellHogaLevels[i].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
            }

            for (int i = 0; i < buyDisplayRows.Count && i < 10; i++)
            {
                long price = buyDisplayRows[i].Price;
                long qty = buyDisplayRows[i].Qty;
                _buyHogaLevels[i].PriceText = price > 0 ? price.ToString("N0") : "-";
                _buyHogaLevels[i].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                _buyHogaLevels[i].RawPrice = price;
                _buyHogaLevels[i].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
            }

            _last0DReceivedAt = DateTime.Now;
            long totalSell = sellDisplayRows.Sum(r => r.Qty);
            long totalBuy = buyDisplayRows.Sum(r => r.Qty);
            UpdateHogaSummary(totalSell, totalBuy);
            HogaStatusText.Text = $"현재가 {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s) && s.CurrentPrice > 0 ? s.CurrentPrice.ToString("N0") : "-")} / 등락률 {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s2) ? s2.ChangeRateText : "-")} / {source} {_last0DReceivedAt:HH:mm:ss}";
            HighlightCenterPriceInHoga();
        }

        private bool TryApplyCurrentPriceFallbackHoga(string stockCode, string source)
        {
            long currentPrice = 0;
            if (_watchStockByCode.TryGetValue(stockCode, out WatchStockItem? stock))
                currentPrice = stock.CurrentPrice;

            if (currentPrice <= 0)
                currentPrice = ParseLongAbs(_currentStatusMetrics.ClosePriceText);

            if (currentPrice <= 0)
                return false;

            for (int i = 0; i < 10; i++)
            {
                _sellHogaLevels[i].PriceText = "-";
                _sellHogaLevels[i].QtyText = "-";
                _sellHogaLevels[i].RateText = string.Empty;
                _sellHogaLevels[i].RawPrice = 0;
                _sellHogaLevels[i].PriceBrush = _whiteBrush;
                _sellHogaLevels[i].RateBrush = _whiteBrush;

                _buyHogaLevels[i].PriceText = "-";
                _buyHogaLevels[i].QtyText = "-";
                _buyHogaLevels[i].RateText = string.Empty;
                _buyHogaLevels[i].RawPrice = 0;
                _buyHogaLevels[i].PriceBrush = _whiteBrush;
                _buyHogaLevels[i].RateBrush = _whiteBrush;
            }

            _sellHogaLevels[9].PriceText = currentPrice.ToString("N0");
            _sellHogaLevels[9].QtyText = "시장가";
            _sellHogaLevels[9].RawPrice = currentPrice;
            _sellHogaLevels[9].PriceBrush = ResolveHogaBrushByKrxPrevClose(currentPrice);

            _buyHogaLevels[0].PriceText = currentPrice.ToString("N0");
            _buyHogaLevels[0].QtyText = "시장가";
            _buyHogaLevels[0].RawPrice = currentPrice;
            _buyHogaLevels[0].PriceBrush = ResolveHogaBrushByKrxPrevClose(currentPrice);

            _last0DReceivedAt = DateTime.Now;
            UpdateHogaSummary(null, null);
            HogaStatusText.Text = $"현재가 {currentPrice:N0} / 등락률 {(_watchStockByCode.TryGetValue(stockCode, out WatchStockItem? s) ? s.ChangeRateText : "-")} / {source} {_last0DReceivedAt:HH:mm:ss}";
            HighlightCenterPriceInHoga();
            AppendLog($"{source}: 호가 없음, 현재가/시장가 기준선 표시: {stockCode}");
            return true;
        }

        private void ApplyRealtimeItem(JsonElement item)
        {
            string rawCode = ReadAnyRealtime(item, "item", "stk_cd", "stkCd", "code", "jm_code", "9001");
            string code = NormalizeStockCode(rawCode);
            if (string.IsNullOrWhiteSpace(code))
                return;

            JsonElement values = item;
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("values", out JsonElement nestedValues))
                values = nestedValues;

            long price = ParseLongAbs(ReadAnyRealtime(item, "cur_prc", "curPrc", "now_prc", "price", "10"));
            long volume = ParseLongAbs(ReadAnyRealtime(values, "acc_trde_qty", "volume", "13"));
            string tradeQtyRaw = ReadAnyRealtime(values, "cntr_qty", "trade_qty", "15");
            long tradeQty = ParseLongAbs(tradeQtyRaw);
            int realtimeSide = ResolveRealtimeTradeSide(values, tradeQtyRaw);
            long sellExecQty = ParseLongAbs(ReadAnyRealtime(item, "mdqty", "sell_exec_qty", "1030"));
            long buyExecQty = ParseLongAbs(ReadAnyRealtime(item, "msqty", "buy_exec_qty", "1031"));
            long sellExecSingleQty = ParseLongAbs(ReadAnyRealtime(item, "sell_exec_single_qty", "1315"));
            long buyExecSingleQty = ParseLongAbs(ReadAnyRealtime(item, "buy_exec_single_qty", "1316"));
            long dayChange = ParseLongSigned(ReadAnyRealtime(item, "pred_pre", "change", "chg_val", "11"));
            string rate = ReadAnyRealtime(item, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (!string.IsNullOrWhiteSpace(rate) && !rate.Contains("%"))
                rate = $"{rate}%";
            if (string.IsNullOrWhiteSpace(rate))
                rate = "-";

            Dispatcher.Invoke(() =>
            {
                if (!_watchStockByCode.TryGetValue(code, out WatchStockItem? stock))
                    return;

                stock.CurrentPrice = price > 0 ? price : stock.CurrentPrice;
                if (volume > 0)
                    stock.VolumeText = volume.ToString("N0");

                long change = code == _selectedStockCode && stock.CurrentPrice > 0 && _krxPrevClosePrice > 0
                    ? stock.CurrentPrice - _krxPrevClosePrice
                    : dayChange != 0 ? dayChange : ResolveChangeByRate(rate);
                stock.ChangeAmount = change;
                stock.ChangeRateText = rate;
                stock.PriceBrush = code == _selectedStockCode && stock.CurrentPrice > 0
                    ? ResolveHogaBrushByKrxPrevClose(stock.CurrentPrice)
                    : change > 0 ? _upColorBrush : change < 0 ? _downColorBrush : _whiteBrush;

                if (code == _selectedStockCode)
                {
                    bool hasSideByRealtimeKey = buyExecQty > 0 || sellExecQty > 0;
                    bool hasSideBySingleKey = buyExecSingleQty > 0 || sellExecSingleQty > 0;
                    bool hasPrevBuy = _lastBuyExecCumByCode.TryGetValue(code, out long prevBuyCum);
                    bool hasPrevSell = _lastSellExecCumByCode.TryGetValue(code, out long prevSellCum);
                    long buyDelta = hasPrevBuy ? Math.Max(0, buyExecQty - prevBuyCum) : 0;
                    long sellDelta = hasPrevSell ? Math.Max(0, sellExecQty - prevSellCum) : 0;
                    bool isBuyAggressive = realtimeSide > 0
                        || (realtimeSide == 0 && hasSideBySingleKey && buyExecSingleQty >= sellExecSingleQty)
                        || (realtimeSide == 0 && hasSideByRealtimeKey && buyDelta >= sellDelta);

                    long lastTick = _lastTickPriceByCode.TryGetValue(code, out long prevTick) ? prevTick : 0;
                    if (realtimeSide == 0 && !hasSideByRealtimeKey && !hasSideBySingleKey && lastTick > 0)
                        isBuyAggressive = stock.CurrentPrice >= lastTick;

                    Brush qtyColor = isBuyAggressive ? _upColorBrush : _downColorBrush;

                    long effectiveTradeQty = tradeQty;
                    if (effectiveTradeQty <= 0 && hasSideByRealtimeKey)
                    {
                        effectiveTradeQty = Math.Max(buyDelta, sellDelta);
                    }
                    if (effectiveTradeQty <= 0 && hasSideBySingleKey)
                    {
                        effectiveTradeQty = Math.Max(buyExecSingleQty, sellExecSingleQty);
                    }

                    _recentTrades.Insert(0, new TradePrint
                    {
                        PriceText = stock.CurrentPrice > 0 ? stock.CurrentPrice.ToString("N0") : "-",
                        QuantityText = effectiveTradeQty > 0 ? effectiveTradeQty.ToString("N0") : "-",
                        Color = ResolveHogaBrushByKrxPrevClose(stock.CurrentPrice),
                        QuantityColor = qtyColor
                    });

                    while (_recentTrades.Count > 10)
                        _recentTrades.RemoveAt(_recentTrades.Count - 1);

                    if (hasSideByRealtimeKey)
                    {
                        _lastBuyExecCumByCode[code] = buyExecQty;
                        _lastSellExecCumByCode[code] = sellExecQty;

                        _buyTradeVolume = buyExecQty;
                        _sellTradeVolume = sellExecQty;
                    }
                    else if (hasSideBySingleKey)
                    {
                        _buyTradeVolume += buyExecSingleQty;
                        _sellTradeVolume += sellExecSingleQty;
                    }
                    else if (effectiveTradeQty > 0)
                    {
                        if (isBuyAggressive)
                            _buyTradeVolume += effectiveTradeQty;
                        else
                            _sellTradeVolume += effectiveTradeQty;
                    }

                    _lastTickPriceByCode[code] = stock.CurrentPrice;
                    HighlightCenterPriceInHoga();

                    long total = _buyTradeVolume + _sellTradeVolume;
                    decimal buyRatio = total > 0 ? (decimal)_buyTradeVolume / total * 100m : 0m;

                    InfoBuyTradeVolumeText.Text = _buyTradeVolume.ToString("N0");
                    InfoSellTradeVolumeText.Text = _sellTradeVolume.ToString("N0");
                    InfoBuyTradeVolumeText.Foreground = _upColorBrush;
                    InfoSellTradeVolumeText.Foreground = _downColorBrush;
                    InfoBuyRatioText.Text = total > 0 ? $"{buyRatio:0}%" : "-";
                    ApplyProgramTradeInfo(_currentStatusMetrics);
                    InfoVolumeText.Text = stock.VolumeText;
                    _currentStatusMetrics.VolumeText = stock.VolumeText;
                    long currentVolume = ParseLongAbs(stock.VolumeText);
                    _currentStatusMetrics.TurnoverRateText = FormatTurnoverRate(currentVolume, ParseLongAbs(_currentStatusMetrics.ListedSharesText));
                    (string dailyVolumeRatioText, Brush dailyVolumeRatioBrush) = FormatDailyVolumeRatio(currentVolume);
                    _currentStatusMetrics.VolumeRatioText = dailyVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Text = dailyVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Foreground = dailyVolumeRatioBrush;
                    SetSelectedStockSubInfo(_currentStatusMetrics, dailyVolumeRatioText, dailyVolumeRatioBrush);
                    InfoTurnoverRateText.Text = _currentStatusMetrics.TurnoverRateText;
                    InfoTradingValueText.Text = _currentStatusMetrics.TradingValueText;

                }
            });
        }

        private void HighlightCenterPriceInHoga()
        {
            UpdateHogaRateMarkers(_sellHogaLevels);
            UpdateHogaRateMarkers(_buyHogaLevels);
        }

        private static int ResolveRealtimeTradeSide(JsonElement values, string quantityText)
        {
            string qty = (quantityText ?? string.Empty).Trim();
            if (qty.StartsWith("+", StringComparison.Ordinal))
                return 1;
            if (qty.StartsWith("-", StringComparison.Ordinal))
                return -1;

            string direction = ReadAnyRealtime(values, "905", "trade_sign", "cntr_sign", "cntr_tp", "trade_tp");
            if (string.IsNullOrWhiteSpace(direction))
                return 0;

            string normalized = direction.Trim().ToUpperInvariant();
            if (normalized == "+" || normalized == "B" || normalized.Contains("BUY") || normalized.Contains("매수"))
                return 1;
            if (normalized == "-" || normalized == "S" || normalized.Contains("SELL") || normalized.Contains("매도"))
                return -1;

            return 0;
        }

        private void UpdateHogaRateMarkers(IEnumerable<HogaLevel> levels)
        {
            foreach (HogaLevel level in levels)
            {
                if (level.RawPrice > 0 && _krxPrevClosePrice > 0)
                {
                    decimal rate = (level.RawPrice - _krxPrevClosePrice) / (decimal)_krxPrevClosePrice * 100m;
                    decimal displayRate = Math.Truncate(rate * 100m) / 100m;
                    level.RateText = $"{displayRate:+0.00;-0.00;0.00}";
                    level.RateBrush = rate > 0 ? _rateUpBrush : rate < 0 ? _rateDownBrush : _whiteBrush;
                }
                else
                {
                    level.RateText = string.Empty;
                    level.RateBrush = _whiteBrush;
                }
            }
        }

        private Brush ResolveHogaBrushByKrxPrevClose(long price)
        {
            if (price <= 0 || _krxPrevClosePrice <= 0)
                return _whiteBrush;

            if (price > _krxPrevClosePrice)
                return _upColorBrush;
            if (price < _krxPrevClosePrice)
                return _downColorBrush;
            return _whiteBrush;
        }

        private async Task RegisterSelectedRealtime0DIfReadyAsync()
        {
            if (_realtimeWs == null || _realtimeWs.State != WebSocketState.Open || _realtimeCts == null)
                return;

            await RegisterSelectedRealtime0DAsync(_realtimeWs, _realtimeCts.Token);
        }

        private async Task RegisterSelectedRealtime0DAsync(ClientWebSocket ws, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_selectedStockCode))
                return;

            string requestCode = ShouldUseNxtDataForStock(_selectedStockCode)
                ? $"{_selectedStockCode}_NX"
                : _selectedStockCode;

            await SendWsJsonAsync(ws, new
            {
                trnm = "REG",
                grp_no = "901",
                refresh = "1",
                data = new[]
                {
                    new
                    {
                        item = new[] { requestCode },
                        type = new[] { "0D", "0H" }
                    }
                }
            }, ct);

            AppendLog($"0D/0H 등록: {requestCode}");
        }

        private static async Task SendWsJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
        {
            string text = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private static async Task<JsonDocument> ReceiveByTrNameAsync(ClientWebSocket ws, string trnm, CancellationToken ct)
        {
            while (true)
            {
                string text = await ReceiveTextAsync(ws, ct);
                JsonDocument doc = JsonDocument.Parse(text);
                string got = ReadString(doc.RootElement, "trnm");
                if (got == "PING")
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                    doc.Dispose();
                    continue;
                }

                if (string.Equals(got, trnm, StringComparison.OrdinalIgnoreCase))
                    return doc;

                doc.Dispose();
            }
        }

        private readonly struct MarketStatusSnapshot
        {
            public MarketStatusSnapshot(string code, string time, string expectedRemain)
            {
                Code = code;
                Time = time;
                ExpectedRemain = expectedRemain;
            }

            public string Code { get; }
            public string Time { get; }
            public string ExpectedRemain { get; }
        }

        private static async Task<MarketStatusSnapshot> ReceiveMarketStatusSnapshotAsync(ClientWebSocket ws, CancellationToken ct)
        {
            while (true)
            {
                string text = await ReceiveTextAsync(ws, ct);
                using JsonDocument doc = JsonDocument.Parse(text);
                JsonElement root = doc.RootElement;
                string trnm = ReadString(root, "trnm");
                if (trnm == "PING")
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                    continue;
                }

                MarketStatusSnapshot status = ExtractMarketStatusSnapshot(root);
                if (!string.IsNullOrWhiteSpace(status.Code))
                    return status;
            }
        }

        private static MarketStatusSnapshot ExtractMarketStatusSnapshot(JsonElement root)
        {
            string status = ReadAnyRealtime(root, "215", "market_status", "marketStatus");
            if (!string.IsNullOrWhiteSpace(status))
                return new MarketStatusSnapshot(status, ReadAnyRealtime(root, "20", "time", "tm"), ReadAnyRealtime(root, "214", "expected_remain", "remain"));

            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return default;

            foreach (JsonElement item in data.EnumerateArray())
            {
                string type = ReadString(item, "type");
                if (!string.Equals(type, "0s", StringComparison.OrdinalIgnoreCase))
                    continue;

                status = ReadAnyRealtime(item, "215", "market_status", "marketStatus");
                if (!string.IsNullOrWhiteSpace(status))
                    return new MarketStatusSnapshot(status, ReadAnyRealtime(item, "20", "time", "tm"), ReadAnyRealtime(item, "214", "expected_remain", "remain"));
            }

            return default;
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[8192];

            while (true)
            {
                WebSocketReceiveResult res = await ws.ReceiveAsync(buffer, ct);
                if (res.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("WS closed");

                ms.Write(buffer, 0, res.Count);
                if (res.EndOfMessage)
                    break;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static string ReadString(JsonElement element, string key)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return string.Empty;

            foreach (JsonProperty p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString();
            }

            return string.Empty;
        }

        private static string ReadAnyRealtime(JsonElement item, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = ReadString(item, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("values", out JsonElement values))
            {
                foreach (string key in keys)
                {
                    string value = ReadString(values, key);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return string.Empty;
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

        private void DayChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Daily;
            _ = RenderSelectedChartAsync(_selectionVersion, _selectedStockCode, _selectedRequestCts?.Token ?? CancellationToken.None);
        }

        private void WeekChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Weekly;
            _ = RenderSelectedChartAsync(_selectionVersion, _selectedStockCode, _selectedRequestCts?.Token ?? CancellationToken.None);
        }

        private void MonthChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Monthly;
            _ = RenderSelectedChartAsync(_selectionVersion, _selectedStockCode, _selectedRequestCts?.Token ?? CancellationToken.None);
        }

        private async Task RenderSelectedChartAsync(int selectionVersion, string selectedStockCode, CancellationToken cancellationToken = default)
        {
            if (MainChartHost == null || VolumeChartHost == null)
                return;

            int count = _currentChartPeriod switch
            {
                ChartPeriod.Daily => 120,
                ChartPeriod.Weekly => 100,
                ChartPeriod.Monthly => 60,
                _ => 120
            };

            if (string.IsNullOrWhiteSpace(selectedStockCode))
                return;

            try
            {
                ChartPeriod requestedPeriod = _currentChartPeriod;
                bool useNxtMarket = IsNxtSupportedStock(selectedStockCode);

                List<ChartCandle> candles = requestedPeriod switch
                {
                    ChartPeriod.Daily => (await _kiwoomConditionService.GetDailyCandlesAsync(selectedStockCode, useNxtMarket, 300, cancellationToken))
                        .TakeLast(count)
                        .Select(ToChartCandle)
                        .ToList(),
                    ChartPeriod.Weekly => (await _kiwoomConditionService.GetWeeklyCandlesAsync(selectedStockCode, useNxtMarket, count, cancellationToken))
                        .Select(ToChartCandle).ToList(),
                    ChartPeriod.Monthly => (await _kiwoomConditionService.GetMonthlyCandlesAsync(selectedStockCode, useNxtMarket, count, cancellationToken))
                        .Select(ToChartCandle).ToList(),
                    _ => (await _kiwoomConditionService.GetDailyCandlesAsync(selectedStockCode, useNxtMarket, 300, cancellationToken))
                        .TakeLast(count)
                        .Select(ToChartCandle)
                        .ToList()
                };
                if (selectionVersion != _selectionVersion || selectedStockCode != _selectedStockCode || requestedPeriod != _currentChartPeriod)
                    return;

                if (candles.Count == 0)
                    return;

                DrawPriceChart(candles);
                DrawVolumeChart(candles);
            }
            catch (OperationCanceledException)
            {
                // selection changed
            }
            catch (Exception ex)
            {
                AppendLog($"차트 조회 오류: {ex.Message}");
            }
        }

        private void DrawPriceChart(List<ChartCandle> candles)
        {
            MainChartHost.Children.Clear();
            var canvas = new Canvas();
            MainChartHost.Children.Add(canvas);

            MainChartHost.UpdateLayout();
            double w = Math.Max(100, MainChartHost.ActualWidth - 2);
            double h = Math.Max(100, MainChartHost.ActualHeight - 2);
            canvas.Width = w;
            canvas.Height = h;
            const double axisWidth = 68;
            double chartW = Math.Max(40, w - axisWidth);
            double currentPrice = ResolveSelectedCurrentPrice(candles.Last().Close);

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
            }

            DrawMovingAverage(canvas, candles, 5, (Brush)FindResource("Ma5Brush"), chartW, h, min, max);
            DrawMovingAverage(canvas, candles, 10, (Brush)FindResource("Ma10Brush"), chartW, h, min, max);
            DrawMovingAverage(canvas, candles, 20, (Brush)FindResource("Ma20Brush"), chartW, h, min, max);
            DrawMovingAverage(canvas, candles, 60, (Brush)FindResource("Ma60Brush"), chartW, h, min, max);

            DrawRightPriceAxis(canvas, chartW, axisWidth, h, min, max, tick);
            DrawCurrentPriceMarker(canvas, chartW, axisWidth, h, min, max, currentPrice);
        }

        private double ResolveSelectedCurrentPrice(double fallback)
        {
            if (!string.IsNullOrWhiteSpace(_selectedStockCode) &&
                _watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? selected) &&
                selected.CurrentPrice > 0)
            {
                return selected.CurrentPrice;
            }

            return fallback;
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

        private void DrawCurrentPriceMarker(Canvas canvas, double chartW, double axisWidth, double h, double min, double max, double currentPrice)
        {
            if (currentPrice <= 0)
                return;

            double range = Math.Max(1, max - min);
            double y = (max - currentPrice) / range * (h - 4) + 2;
            y = Math.Max(1, Math.Min(h - 1, y));
            Brush markerBrush = ResolveHogaBrushByKrxPrevClose((long)Math.Round(currentPrice));

            canvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = chartW + 10,
                Y1 = y,
                Y2 = y,
                Stroke = markerBrush,
                StrokeThickness = 1,
                Opacity = 0.75
            });

            var label = new Border
            {
                Background = (Brush)FindResource("BgPanelBrush"),
                BorderBrush = markerBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = currentPrice.ToString("N0"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = markerBrush
                }
            };

            Canvas.SetLeft(label, chartW + 12);
            Canvas.SetTop(label, Math.Max(0, Math.Min(h - 18, y - 9)));
            canvas.Children.Add(label);
        }

        private void DrawMovingAverage(Canvas canvas, List<ChartCandle> candles, int period, Brush color, double w, double h, double min, double max)
        {
            if (candles.Count < period) return;

            double range = Math.Max(1, max - min);
            double gap = w / candles.Count;
            var points = new PointCollection();

            for (int i = period - 1; i < candles.Count; i++)
            {
                double avg = candles.Skip(i - period + 1).Take(period).Average(c => c.Close);
                double x = i * gap + gap / 2;
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

        private void DrawVolumeChart(List<ChartCandle> candles)
        {
            VolumeChartHost.Children.Clear();
            var canvas = new Canvas();
            VolumeChartHost.Children.Add(canvas);

            VolumeChartHost.UpdateLayout();
            double w = Math.Max(100, VolumeChartHost.ActualWidth - 2);
            double h = Math.Max(40, VolumeChartHost.ActualHeight - 2);
            canvas.Width = w;
            canvas.Height = h;
            const double axisWidth = 68;
            double chartW = Math.Max(40, w - axisWidth);

            long maxVol = Math.Max(1, candles.Max(c => c.Volume));
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
                    Height = Math.Max(1, barH),
                    Fill = c.Close >= c.Open ? _upColorBrush : _downColorBrush,
                    Opacity = 0.8
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, h - bar.Height);
                canvas.Children.Add(bar);
            }

            DrawRightVolumeAxis(canvas, chartW, axisWidth, h, maxVol);
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
                return $"{value / 100_000_000d:0.#}억";
            if (value >= 10_000)
                return $"{value / 10_000d:0.#}만";
            return value.ToString("N0");
        }

        private enum ChartPeriod
        {
            Daily,
            Weekly,
            Monthly
        }

        private sealed class ChartCandle
        {
            public double Open { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public double Close { get; set; }
            public long Volume { get; set; }
        }

        private static ChartCandle ToChartCandle(DailyCandle c) => new ChartCandle
        {
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume
        };


    }
}

