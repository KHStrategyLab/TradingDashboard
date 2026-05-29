using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using TradingDashboard.Models;

namespace TradingDashboard
{
    public partial class MainWindow
    {
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
                    StartSelectedChartRender();
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
            long curNum = ParseLongAbs(cur);

            Dispatcher.Invoke(() =>
            {
                if (curNum > 0 && _watchStockByCode.TryGetValue(code, out WatchStockItem? stock))
                {
                    stock.CurrentPrice = curNum;
                    stock.ChangeRateText = FormatKrxPreviousCloseRate(curNum);
                }

                HogaStatusText.Text = $"현재가 {(curNum > 0 ? curNum.ToString("N0") : "-")} / 등락률 {FormatKrxPreviousCloseRate(curNum)} / 0D {(_last0DReceivedAt == DateTime.MinValue ? "-" : _last0DReceivedAt.ToString("HH:mm:ss"))}";
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
            string tradeTimeText = ReadAnyRealtime(values, "cntr_tm", "time", "20");
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
                stock.ChangeRateText = code == _selectedStockCode
                    ? FormatKrxPreviousCloseRate(stock.CurrentPrice)
                    : rate;
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
                    ApplyRealtimeChartTick(code, stock.CurrentPrice, volume > 0 ? currentVolume : 0, effectiveTradeQty, tradeTimeText);

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

        private string FormatKrxPreviousCloseRate(long price)
        {
            if (price <= 0 || _krxPrevClosePrice <= 0)
                return "-";

            decimal rate = (price - _krxPrevClosePrice) / (decimal)_krxPrevClosePrice * 100m;
            decimal displayRate = Math.Truncate(rate * 100m) / 100m;
            return $"{displayRate:+0.00;-0.00;0.00}%";
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


    }
}
