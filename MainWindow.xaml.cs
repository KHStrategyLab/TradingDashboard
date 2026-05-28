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
        private const int MaxLogLines = 120;
        private readonly AppConfig _config;
        private readonly NaverNewsService _newsService;
        private readonly KiwoomRestConditionService _kiwoomConditionService;
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
        private ClientWebSocket? _realtimeWs;
        private CancellationTokenSource? _realtimeCts;
        private static readonly string[] SellPriceKeys = { "41", "42", "43", "44", "45", "46", "47", "48", "49", "50" };
        private static readonly string[] SellQtyKeys = { "61", "62", "63", "64", "65", "66", "67", "68", "69", "70" };
        private static readonly string[] BuyPriceKeys = { "51", "52", "53", "54", "55", "56", "57", "58", "59", "60" };
        private static readonly string[] BuyQtyKeys = { "71", "72", "73", "74", "75", "76", "77", "78", "79", "80" };

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
            _kiwoomConditionService = new KiwoomRestConditionService(_config.Kiwoom);
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
            HogaSummaryText.Text = "총매도 0 · 총매수 0 · 차이 0";

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppendLog("앱 시작");
            await LoadWatchListFromKiwoomConditionAsync();

            if (WatchListBox.SelectedItem is not ListBoxItem)
            {
                WatchListBox.SelectedIndex = 0;
            }

            await LoadNewsForSelectedStockAsync();
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
                AppendLog($"조건식 결과 {stocks.Count}건 반영");
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
                .Where(stock => stock != null && !string.IsNullOrWhiteSpace(stock.Name))
                .GroupBy(stock => stock.Code ?? string.Empty, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            if (cleanStocks.Count == 0)
                return;

            _watchStocks.Clear();
            _watchStockByCode.Clear();

            foreach (WatchStockItem stock in cleanStocks)
            {
                stock.PriceBrush = stock.ChangeAmount > 0 ? _upColorBrush : stock.ChangeAmount < 0 ? _downColorBrush : _whiteBrush;
                _watchStocks.Add(stock);
                if (!string.IsNullOrWhiteSpace(stock.Code))
                    _watchStockByCode[stock.Code] = stock;
            }

            AppendLog("왼쪽 목록 갱신");
        }

        private async void WatchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await LoadNewsForSelectedStockAsync();
        }

        private Task LoadNewsForSelectedStockAsync()
        {
            string stockName = WatchListBox.SelectedItem switch
            {
                WatchStockItem stock => stock.Name,
                ListBoxItem listBoxItem => listBoxItem.Tag as string ?? string.Empty,
                _ => string.Empty
            };
            string stockCode = WatchListBox.SelectedItem is WatchStockItem selectedWatch ? selectedWatch.Code : string.Empty;
            if (string.IsNullOrWhiteSpace(stockName))
                return Task.CompletedTask;

            int selectionVersion = ++_selectionVersion;
            SelectedStockTitle.Text = stockName;
            _selectedStockCode = stockCode;
            _recentTrades.Clear();
            _buyTradeVolume = 0;
            _sellTradeVolume = 0;
            _selectedPreviousVolume = 0;
            _lastTickPriceByCode.Clear();
            _lastBuyExecCumByCode.Clear();
            _lastSellExecCumByCode.Clear();
            HogaStatusText.Text = "현재가 - / 등락률 - / 0D -";
            HogaSummaryText.Text = "총매도 0 · 총매수 0 · 차이 0";
            AppendLog($"종목 선택: {stockName}");

            _ = RenderSelectedChartAsync(selectionVersion, stockCode);
            _ = LoadNewsAsync(stockName, selectionVersion);
            _ = LoadSelectedStockStatusAsync(stockCode, selectionVersion);
            _ = LoadKrxClosingSnapshotIfNeededAsync(stockCode, selectionVersion);
            _ = RegisterSelectedRealtime0DIfReadyAsync();
            return Task.CompletedTask;
        }

        private async Task LoadNewsAsync(string stockName, int selectionVersion)
        {
            try
            {
                var news = await _newsService.GetLatestNewsAsync(stockName, 5);
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

        private async Task LoadSelectedStockStatusAsync(string stockCode, int selectionVersion)
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

                bool useNxtMarket = _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected) && selected.SupportsNxt;
                StockStatusMetrics m = await _kiwoomConditionService.GetStockStatusMetricsByGuideAsync(stockCode, useNxtMarket);
                StockStatusMetrics krxMetrics = useNxtMarket
                    ? await _kiwoomConditionService.GetStockStatusMetricsByGuideAsync(stockCode, false)
                    : m;
                _currentStatusMetrics = m;
                _krxPrevClosePrice = ParseLongAbs(krxMetrics.BasePriceText ?? string.Empty);
                (string dailyVolumeRatioText, Brush dailyVolumeRatioBrush) = await GetDailyVolumeRatioAsync(stockCode, useNxtMarket, m);
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
                InfoBasePriceText.Text = m.BasePriceText;
                ApplySelectedPriceInfoColors(m);
                InfoTradingValueText.Text = m.TradingValueText;
                InfoVolumeText.Text = m.VolumeText;
                InfoPrevTimeVolumeRatioText.Text = m.VolumeRatioText;
                InfoPrevTimeVolumeRatioText.Foreground = dailyVolumeRatioBrush;
                InfoTurnoverRateText.Text = m.TurnoverRateText;
                StockStatusMetrics exec = await _kiwoomConditionService.GetTodayExecutionSummaryAsync(stockCode, useNxtMarket);
                _buyTradeVolume = Math.Max(0, exec.BuyExecCum);
                _sellTradeVolume = Math.Max(0, exec.SellExecCum);
                _lastBuyExecCumByCode[stockCode] = _buyTradeVolume;
                _lastSellExecCumByCode[stockCode] = _sellTradeVolume;

                long total = _buyTradeVolume + _sellTradeVolume;
                decimal buyRatio = total > 0 ? (decimal)_buyTradeVolume / total * 100m : 0m;
                InfoSellTradeVolumeText.Text = _sellTradeVolume.ToString("N0");
                InfoBuyTradeVolumeText.Text = _buyTradeVolume.ToString("N0");
                InfoBuyRatioText.Text = total > 0 ? $"{buyRatio:0}%" : "-";
                InfoProgramText.Text = exec.ProgramBuyText == "-" ? m.ListedSharesText : exec.ProgramBuyText;
            }
            catch (Exception ex)
            {
                AppendLog($"상태표시줄 조회 오류: {ex.Message}");
                SetSelectedStockSubInfo(new StockStatusMetrics(), "-", _whiteBrush);
            }
        }

        private async Task<(string Text, Brush Brush)> GetDailyVolumeRatioAsync(string stockCode, bool useNxtMarket, StockStatusMetrics metrics)
        {
            long todayVolume = ParseLongAbs(metrics.VolumeText);
            if (todayVolume <= 0)
                return ("-", _whiteBrush);

            List<DailyCandle> candles = await _kiwoomConditionService.GetDailyCandlesAsync(stockCode, useNxtMarket, 5);
            string today = DateTime.Now.ToString("yyyyMMdd");
            DailyCandle? previousTradingDay = candles
                .Where(c => !string.Equals(c.Date, today, StringComparison.Ordinal))
                .OrderBy(c => c.Date)
                .LastOrDefault();

            long previousVolume = previousTradingDay?.Volume ?? 0;
            _selectedPreviousVolume = previousVolume;
            if (previousVolume <= 0)
                return ("-", _whiteBrush);

            return FormatDailyVolumeRatio(todayVolume);
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

        private void SetSelectedStockSubInfo(StockStatusMetrics metrics, string volumeRatioText, Brush volumeRatioBrush)
        {
            SelectedStockSubInfo.Inlines.Clear();
            SelectedStockSubInfo.Inlines.Add(new Run(
                $"거래량 {metrics.VolumeText} · 거래대금 {metrics.TradingValueText} · 시가총액 {metrics.MarketCapText} · 유통주식수 {metrics.ListedSharesText} · 회전율 {metrics.TurnoverRateText} · 주가등락률 {metrics.ChangeRateText} · 전일거래량대비 금일거래량 비율 "));
            SelectedStockSubInfo.Inlines.Add(new Run(volumeRatioText)
            {
                Foreground = volumeRatioBrush,
                FontWeight = FontWeights.SemiBold
            });
        }

        private async Task LoadKrxClosingSnapshotIfNeededAsync(string stockCode, int selectionVersion)
        {
            try
            {
                if (!_config.Kiwoom.UseRestApi || string.IsNullOrWhiteSpace(stockCode))
                    return;
                if (selectionVersion != _selectionVersion)
                    return;

                bool supportsNxt = _watchStockByCode.TryGetValue(stockCode, out WatchStockItem? selected) && selected.SupportsNxt;
                bool useNxtSnapshot = supportsNxt && IsNxtFrozenWindow();

                if (!IsKrxRegularClosedWindow() || (supportsNxt && IsNxtAfterMarketWindow()))
                    return;

                KrxClosingSnapshot snapshot = await _kiwoomConditionService.GetKrxClosingSnapshotAsync(stockCode, useNxtSnapshot);
                if (selectionVersion != _selectionVersion || stockCode != _selectedStockCode)
                    return;

                ApplyClosingSnapshot(snapshot, useNxtSnapshot ? "NXT 20시 최종" : "KRX 종가");
                AppendLog($"{(useNxtSnapshot ? "NXT 20시 최종" : "KRX 종가")} 스냅샷 적용: {stockCode}");
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

            bool isNxtSnapshot = source.StartsWith("NXT", StringComparison.OrdinalIgnoreCase);
            if (snapshot.BasePrice > 0 && (!isNxtSnapshot || _krxPrevClosePrice <= 0))
                _krxPrevClosePrice = snapshot.BasePrice;

            if (_watchStockByCode.TryGetValue(snapshot.Code, out WatchStockItem? stock))
            {
                if (snapshot.CurrentPrice > 0)
                    stock.CurrentPrice = snapshot.CurrentPrice;
                stock.ChangeAmount = snapshot.DayChange;
                stock.ChangeRateText = snapshot.ChangeRateText;
                stock.PriceBrush = ResolveHogaBrushByKrxPrevClose(stock.CurrentPrice);
            }

            ApplyHogaRows(snapshot.SellLevels.Select(r => (r.Price, r.Quantity)).ToList(), snapshot.BuyLevels.Select(r => (r.Price, r.Quantity)).ToList(), $"{source} 스냅샷");
            if (snapshot.CurrentPrice > 0)
            {
                InfoBasePriceText.Text = _krxPrevClosePrice > 0 ? _krxPrevClosePrice.ToString("N0") : "-";
                InfoBasePriceText.Foreground = _whiteBrush;
            }

            _buyTradeVolume = Math.Max(0, snapshot.BuyExecCum);
            _sellTradeVolume = Math.Max(0, snapshot.SellExecCum);
            _lastBuyExecCumByCode[snapshot.Code] = _buyTradeVolume;
            _lastSellExecCumByCode[snapshot.Code] = _sellTradeVolume;
            UpdateTradeSummaryInfo();

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

        private void ApplySelectedPriceInfoColors(StockStatusMetrics metrics)
        {
            InfoOpenPriceText.Foreground = ResolveHogaBrushByKrxPrevClose(ParseLongAbs(metrics.OpenPriceText));
            InfoHighPriceText.Foreground = ResolveHogaBrushByKrxPrevClose(ParseLongAbs(metrics.HighPriceText));
            InfoLowPriceText.Foreground = ResolveHogaBrushByKrxPrevClose(ParseLongAbs(metrics.LowPriceText));
            InfoBasePriceText.Foreground = _whiteBrush;
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

            if (!IsNxtAfterMarketWindow())
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

        private static bool IsNxtAfterMarketWindow()
        {
            TimeSpan now = DateTime.Now.TimeOfDay;
            return now >= new TimeSpan(15, 40, 0) && now < new TimeSpan(20, 0, 0);
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
                if (string.Equals(type, "0D", StringComparison.OrdinalIgnoreCase))
                    ApplyRealtimeHogaItem(item);
                else if (string.Equals(type, "0H", StringComparison.OrdinalIgnoreCase))
                    ApplyRealtimeExpectedItem(item);
                else
                    ApplyRealtimeItem(item);
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
                    long sellPriceNum = ParseLongAbs(ReadAnyRealtime(values, SellPriceKeys[i], $"sel_{i + 1}bid", $"sell_{i + 1}_price"));
                    long sellQtyNum = ParseLongAbs(ReadAnyRealtime(values, SellQtyKeys[i], $"sel_{i + 1}bid_req", $"sell_{i + 1}_qty"));
                    sellRows.Add((sellPriceNum, sellQtyNum));

                    long buyPriceNum = ParseLongAbs(ReadAnyRealtime(values, BuyPriceKeys[i], $"buy_{i + 1}bid", $"buy_{i + 1}_price"));
                    long buyQtyNum = ParseLongAbs(ReadAnyRealtime(values, BuyQtyKeys[i], $"buy_{i + 1}bid_req", $"buy_{i + 1}_qty"));
                    buyRows.Add((buyPriceNum, buyQtyNum));
                }

                var sellActiveRows = sellRows
                    .Where(r => r.Price > 0 || r.Qty > 0)
                    .OrderByDescending(r => r.Price)
                    .Take(10)
                    .ToList();
                var buyActiveRows = buyRows
                    .Where(r => r.Price > 0 || r.Qty > 0)
                    .OrderByDescending(r => r.Price)
                    .Take(10)
                    .ToList();

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

                int sellStartIndex = Math.Max(0, 10 - sellActiveRows.Count); // 상단 매도는 아래 기준 정렬
                for (int i = 0; i < sellActiveRows.Count; i++)
                {
                    int target = sellStartIndex + i;
                    long price = sellActiveRows[i].Price;
                    long qty = sellActiveRows[i].Qty;
                    _sellHogaLevels[target].PriceText = price > 0 ? price.ToString("N0") : "-";
                    _sellHogaLevels[target].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                    _sellHogaLevels[target].RawPrice = price;
                    _sellHogaLevels[target].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
                }

                for (int i = 0; i < buyActiveRows.Count; i++) // 하단 매수는 위 기준 정렬
                {
                    long price = buyActiveRows[i].Price;
                    long qty = buyActiveRows[i].Qty;
                    _buyHogaLevels[i].PriceText = price > 0 ? price.ToString("N0") : "-";
                    _buyHogaLevels[i].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                    _buyHogaLevels[i].RawPrice = price;
                    _buyHogaLevels[i].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
                }

                _last0DReceivedAt = DateTime.Now;
                long totalSell = sellActiveRows.Sum(r => r.Qty);
                long totalBuy = buyActiveRows.Sum(r => r.Qty);
                long diff = totalSell - totalBuy;
                HogaSummaryText.Text = $"총매도 {totalSell:N0} · 총매수 {totalBuy:N0} · 차이 {diff:N0}";
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
            var sellActiveRows = sellRows
                .Where(r => r.Price > 0 || r.Qty > 0)
                .OrderByDescending(r => r.Price)
                .Take(10)
                .ToList();
            var buyActiveRows = buyRows
                .Where(r => r.Price > 0 || r.Qty > 0)
                .OrderByDescending(r => r.Price)
                .Take(10)
                .ToList();

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

            int sellStartIndex = Math.Max(0, 10 - sellActiveRows.Count);
            for (int i = 0; i < sellActiveRows.Count; i++)
            {
                int target = sellStartIndex + i;
                long price = sellActiveRows[i].Price;
                long qty = sellActiveRows[i].Qty;
                _sellHogaLevels[target].PriceText = price > 0 ? price.ToString("N0") : "-";
                _sellHogaLevels[target].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                _sellHogaLevels[target].RawPrice = price;
                _sellHogaLevels[target].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
            }

            for (int i = 0; i < buyActiveRows.Count; i++)
            {
                long price = buyActiveRows[i].Price;
                long qty = buyActiveRows[i].Qty;
                _buyHogaLevels[i].PriceText = price > 0 ? price.ToString("N0") : "-";
                _buyHogaLevels[i].QtyText = qty > 0 ? qty.ToString("N0") : "-";
                _buyHogaLevels[i].RawPrice = price;
                _buyHogaLevels[i].PriceBrush = ResolveHogaBrushByKrxPrevClose(price);
            }

            _last0DReceivedAt = DateTime.Now;
            long totalSell = sellActiveRows.Sum(r => r.Qty);
            long totalBuy = buyActiveRows.Sum(r => r.Qty);
            long diff = totalSell - totalBuy;
            HogaSummaryText.Text = $"총매도 {totalSell:N0} · 총매수 {totalBuy:N0} · 차이 {diff:N0}";
            HogaStatusText.Text = $"현재가 {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s) && s.CurrentPrice > 0 ? s.CurrentPrice.ToString("N0") : "-")} / 등락률 {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s2) ? s2.ChangeRateText : "-")} / {source} {_last0DReceivedAt:HH:mm:ss}";
            HighlightCenterPriceInHoga();
        }

        private void ApplyRealtimeItem(JsonElement item)
        {
            string rawCode = ReadAnyRealtime(item, "item", "stk_cd", "stkCd", "code", "jm_code", "9001");
            string code = NormalizeStockCode(rawCode);
            if (string.IsNullOrWhiteSpace(code))
                return;

            long price = ParseLongAbs(ReadAnyRealtime(item, "cur_prc", "curPrc", "now_prc", "price", "10"));
            long volume = ParseLongAbs(ReadAnyRealtime(item, "acc_trde_qty", "trde_qty", "volume", "13", "15"));
            string tradeQtyRaw = ReadAnyRealtime(item, "cntr_qty", "trade_qty", "15");
            long signedTradeQty = ParseLongSigned(tradeQtyRaw);
            long tradeQty = Math.Abs(signedTradeQty);
            long sellExecQty = ParseLongAbs(ReadAnyRealtime(item, "mdqty", "sell_exec_qty", "1030"));
            long buyExecQty = ParseLongAbs(ReadAnyRealtime(item, "msqty", "buy_exec_qty", "1031"));
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

                long change = dayChange != 0 ? dayChange : ResolveChangeByRate(rate);
                stock.ChangeAmount = change;
                stock.ChangeRateText = rate;
                stock.PriceBrush = code == _selectedStockCode && stock.CurrentPrice > 0
                    ? ResolveHogaBrushByKrxPrevClose(stock.CurrentPrice)
                    : change > 0 ? _upColorBrush : change < 0 ? _downColorBrush : _whiteBrush;

                if (code == _selectedStockCode)
                {
                    bool hasSideByRealtimeKey = buyExecQty > 0 || sellExecQty > 0;
                    bool hasPrevBuy = _lastBuyExecCumByCode.TryGetValue(code, out long prevBuyCum);
                    bool hasPrevSell = _lastSellExecCumByCode.TryGetValue(code, out long prevSellCum);
                    long buyDelta = hasPrevBuy ? Math.Max(0, buyExecQty - prevBuyCum) : 0;
                    long sellDelta = hasPrevSell ? Math.Max(0, sellExecQty - prevSellCum) : 0;
                    bool isBuyAggressive = signedTradeQty > 0 || (signedTradeQty == 0 && buyDelta >= sellDelta);

                    long lastTick = _lastTickPriceByCode.TryGetValue(code, out long prevTick) ? prevTick : 0;
                    if (signedTradeQty == 0 && !hasSideByRealtimeKey && lastTick > 0)
                        isBuyAggressive = stock.CurrentPrice >= lastTick;

                    Brush qtyColor = isBuyAggressive ? _upColorBrush : _downColorBrush;

                    long effectiveTradeQty = tradeQty;
                    if (effectiveTradeQty <= 0 && hasSideByRealtimeKey)
                    {
                        effectiveTradeQty = Math.Max(buyDelta, sellDelta);
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

                        _buyTradeVolume += buyDelta;
                        _sellTradeVolume += sellDelta;
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
                    InfoProgramText.Text = _currentStatusMetrics.ListedSharesText;
                    InfoVolumeText.Text = stock.VolumeText;
                    _currentStatusMetrics.VolumeText = stock.VolumeText;
                    long currentVolume = ParseLongAbs(stock.VolumeText);
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

            string requestCode = _selectedStockCode;
            if (_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? selected) && selected.SupportsNxt && IsNxtAfterMarketWindow())
                requestCode = $"{_selectedStockCode}_NX";

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
            _ = RenderSelectedChartAsync(_selectionVersion, _selectedStockCode);
        }

        private void WeekChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Weekly;
            _ = RenderSelectedChartAsync(_selectionVersion, _selectedStockCode);
        }

        private void MonthChartButton_Click(object sender, RoutedEventArgs e)
        {
            _currentChartPeriod = ChartPeriod.Monthly;
            _ = RenderSelectedChartAsync(_selectionVersion, _selectedStockCode);
        }

        private async Task RenderSelectedChartAsync(int selectionVersion, string selectedStockCode)
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
                bool useNxtMarket = _watchStockByCode.TryGetValue(selectedStockCode, out WatchStockItem? selected)
                    && selected.SupportsNxt;

                List<ChartCandle> candles = requestedPeriod switch
                {
                    ChartPeriod.Daily => (await _kiwoomConditionService.GetDailyCandlesAsync(selectedStockCode, useNxtMarket, 300))
                        .TakeLast(count)
                        .Select(ToChartCandle)
                        .ToList(),
                    ChartPeriod.Weekly => (await _kiwoomConditionService.GetWeeklyCandlesAsync(selectedStockCode, useNxtMarket, count))
                        .Select(ToChartCandle).ToList(),
                    ChartPeriod.Monthly => (await _kiwoomConditionService.GetMonthlyCandlesAsync(selectedStockCode, useNxtMarket, count))
                        .Select(ToChartCandle).ToList(),
                    _ => (await _kiwoomConditionService.GetDailyCandlesAsync(selectedStockCode, useNxtMarket, 300))
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

