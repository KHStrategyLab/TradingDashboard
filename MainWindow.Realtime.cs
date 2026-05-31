using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
                AppendLog("0B WS connected");

                await SendWsJsonAsync(_realtimeWs, new { trnm = "LOGIN", token }, ct);
                using JsonDocument login = await ReceiveByTrNameAsync(_realtimeWs, "LOGIN", ct);
                string loginCode = ReadString(login.RootElement, "return_code");
                if (loginCode != "0")
                {
                    AppendLog($"0B LOGIN failed: {loginCode}");
                    return;
                }

                await RegisterConditionRealtimeAsync(_realtimeWs, ct);
                await RegisterMarketStatusAsync(_realtimeWs, ct);
                await RegisterRealtime0BAsync(_realtimeWs, ct);
                await RegisterSelectedRealtime0DAsync(_realtimeWs, ct);
                AppendLog($"0B registration complete: {_watchStockByCode.Count}stocks");

                _ = Task.Run(() => ReceiveRealtimeLoopAsync(_realtimeWs, ct), ct);
            }
            catch (Exception ex)
            {
                AppendLog($"0B start error: {ex.Message}");
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

            AppendLog("0s market status registered");
        }

        private async Task RegisterConditionRealtimeAsync(ClientWebSocket ws, CancellationToken ct)
        {
            try
            {
                await SendWsJsonAsync(ws, new { trnm = "CNSRLST" }, ct);
                using JsonDocument list = await ReceiveByTrNameAsync(ws, "CNSRLST", ct);
                string seq = ResolveConditionRealtimeSeq(list.RootElement, _config.Kiwoom.ConditionSeq01);
                if (string.IsNullOrWhiteSpace(seq))
                {
                    AppendLog($"condition realtime tracking failed: condition {(_config.Kiwoom.ConditionSeq01 ?? "1")} not found");
                    return;
                }

                _conditionRealtimeSeq = seq;
                await SendWsJsonAsync(ws, new
                {
                    trnm = "CNSRREQ",
                    seq,
                    search_type = "1",
                    stex_tp = "K",
                    cont_yn = "N",
                    next_key = ""
                }, ct);

                using JsonDocument response = await ReceiveByTrNameAsync(ws, "CNSRREQ", ct);
                string returnCode = ReadString(response.RootElement, "return_code");
                if (!string.IsNullOrWhiteSpace(returnCode) && returnCode != "0")
                {
                    AppendLog($"condition realtime tracking failed: {returnCode} {ReadString(response.RootElement, "return_msg")}");
                    return;
                }

                int initialCount = response.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array
                    ? data.GetArrayLength()
                    : 0;
                AppendLog($"condition realtime tracking registered: {seq} ({initialCount}items)");
            }
            catch (Exception ex)
            {
                AppendLog($"condition realtime tracking registration error: {ex.Message}");
            }
        }

        private async Task RegisterRealtime0BAsync(ClientWebSocket ws, CancellationToken ct)
        {
            string[] krxItems = [.. _watchStockByCode.Keys
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.Ordinal)];

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

            string[] nxtItems = [.. _watchStockByCode
                .Where(kv => kv.Value.SupportsNxt)
                .Select(kv => $"{kv.Key}_NX")
                .Distinct(StringComparer.OrdinalIgnoreCase)];

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

            AppendLog($"0B NXT after-market registered: {nxtItems.Length}stocks");
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
                return IsNxtMarketWindow();

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
                "0" => "KRX pre-open notice",
                "3" => "KRX open",
                "2" => "KRX close notice",
                "4" => "KRX closed",
                "8" => "KRX regular closed",
                "9" => "all markets closed",
                "a" => "KRX after-hours close trading start",
                "b" => "KRX after-hours close trading end",
                "c" => "KRX after-hours single-price start",
                "d" => "KRX after-hours single-price end",
                "e" => "futures/options closing auction end",
                "f" => "futures/options session notice",
                "o" => "futures/options open",
                "s" => "futures/options closing auction start",
                "P" => "NXT pre-market start",
                "Q" => "NXT pre-market end",
                "R" => "NXT main market start",
                "S" => "NXT main market end",
                "T" => "NXT after-market single-price start",
                "U" => "NXT after-market start",
                "V" => "NXT after-market end",
                _ => string.IsNullOrWhiteSpace(code) ? "Market status unknown" : $"undefined market status({code})"
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
                Dispatcher.Invoke(() => AppendLog($"0B receive error: {ex.Message}"));
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
                else if (IsConditionRealtimeItem(item))
                    ApplyConditionRealtimeItem(item);
                else
                    ApplyRealtimeItem(item);
            }
        }

        private static bool IsConditionRealtimeItem(JsonElement item)
        {
            string name = ReadString(item, "name");
            string eventCode = ReadAnyRealtime(item, "841");
            return name.Contains("조건", StringComparison.OrdinalIgnoreCase) ||
                eventCode == "4" ||
                eventCode == "5";
        }

        private void ApplyConditionRealtimeItem(JsonElement item)
        {
            string type = ReadString(item, "type");
            string eventCode = ReadAnyRealtime(item, "841");
            bool isAdd = eventCode == "4" || string.Equals(type, "02", StringComparison.OrdinalIgnoreCase);
            bool isRemove = eventCode == "5" || string.Equals(type, "03", StringComparison.OrdinalIgnoreCase);
            if (!isAdd && !isRemove)
                return;

            string code = NormalizeStockCode(ReadAnyRealtime(item, "item", "9001", "stk_cd", "stkCd", "code", "jm_code"));
            if (string.IsNullOrWhiteSpace(code))
                return;

            string name = ReadAnyRealtime(item, "302", "stk_nm", "stkNm", "name", "jm_name");
            if (isAdd)
            {
                (bool shouldStart, long generation) = MarkConditionRealtimeEnter(code);
                if (shouldStart)
                    _ = ApplyConditionRealtimeAddAsync(code, name, generation);
            }
            else
                ApplyConditionRealtimeRemove(code);
        }

        private (bool ShouldStart, long Generation) MarkConditionRealtimeEnter(string code)
        {
            lock (_conditionRealtimeEnterPendingLock)
            {
                long generation = ++_conditionRealtimeGenerationSequence;
                _conditionRealtimeActiveCodes.Add(code);
                _conditionRealtimeGenerationByCode[code] = generation;
                return (_conditionRealtimeEnterPendingCodes.Add(code), generation);
            }
        }

        private void MarkConditionRealtimeExit(string code)
        {
            lock (_conditionRealtimeEnterPendingLock)
            {
                long generation = ++_conditionRealtimeGenerationSequence;
                _conditionRealtimeActiveCodes.Remove(code);
                _conditionRealtimeGenerationByCode[code] = generation;
            }
        }

        private bool IsConditionRealtimeEnterCurrent(string code, long generation)
        {
            lock (_conditionRealtimeEnterPendingLock)
            {
                return _conditionRealtimeActiveCodes.Contains(code) &&
                    _conditionRealtimeGenerationByCode.TryGetValue(code, out long currentGeneration) &&
                    currentGeneration == generation;
            }
        }

        private (bool ShouldRestart, long Generation) ClearConditionRealtimeEnterPending(string code, long completedGeneration)
        {
            lock (_conditionRealtimeEnterPendingLock)
            {
                _conditionRealtimeEnterPendingCodes.Remove(code);
                if (_conditionRealtimeActiveCodes.Contains(code) &&
                    _conditionRealtimeGenerationByCode.TryGetValue(code, out long latestGeneration) &&
                    latestGeneration != completedGeneration)
                {
                    _conditionRealtimeEnterPendingCodes.Add(code);
                    return (true, latestGeneration);
                }

                return (false, 0);
            }
        }

        private async Task ApplyConditionRealtimeAddAsync(string code, string name, long generation)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                    return;

                CancellationToken ct = _realtimeCts?.Token ?? CancellationToken.None;
                await _conditionRealtimeEnterSemaphore.WaitAsync(ct);
                try
                {
                    if (!IsConditionRealtimeEnterCurrent(code, generation))
                        return;

                    (bool handledFromCache, WatchStockItem? addedFromCache) = await ApplyConditionRealtimeAddFromCacheAsync(code, name, generation);
                    if (handledFromCache)
                    {
                        if (!IsConditionRealtimeEnterCurrent(code, generation))
                            return;

                        if (addedFromCache != null)
                        {
                            await TrySendConditionEnterAlertAsync(addedFromCache, "cache re-enter", ct);
                            if (!IsConditionRealtimeEnterCurrent(code, generation))
                                return;
                            await _disclosureAlertService.TrySendRecentDisclosureAlertAsync(addedFromCache, ct);
                            if (!IsConditionRealtimeEnterCurrent(code, generation))
                                return;
                            await TrySendLateNewsAlertAsync(addedFromCache, ct);
                        }

                        await RegisterRealtime0BForCurrentWatchlistAsync();
                        return;
                    }

                    WatchStockItem stock = await _kiwoomConditionService.GetConditionStockAsync(code, name, ct);
                    if (!IsConditionRealtimeEnterCurrent(code, generation))
                        return;

                    // 장중 실시간 편입은 별도 일봉 게이트 재조회 없이 NEW로만 넘긴다.
                    // 기준봉 통과 여부는 초기 조건식 조회 또는 오늘 캐시에 있는 게이트 결과만 신뢰한다.
                    stock.IsIntradayPreCandidate = true;
                    Dispatcher.Invoke(() => AppendLog($"condition enter NEW: {stock.Name} ({stock.Code})"));

                    bool added = false;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!IsConditionRealtimeEnterCurrent(code, generation))
                            return;

                        if (_watchStockByCode.ContainsKey(code))
                            return;

                        if (string.IsNullOrWhiteSpace(stock.Code))
                            stock.Code = code;
                        if (string.IsNullOrWhiteSpace(stock.Name))
                            stock.Name = string.IsNullOrWhiteSpace(name) ? code : name;
                        if (string.IsNullOrWhiteSpace(stock.ChangeRateText))
                            stock.ChangeRateText = "-";
                        if (string.IsNullOrWhiteSpace(stock.VolumeText))
                            stock.VolumeText = "-";

                        stock.PriceBrush = stock.ChangeAmount > 0 ? _upColorBrush : stock.ChangeAmount < 0 ? _downColorBrush : _whiteBrush;
                        ApplyWatchlistCacheToStock(stock);
                        ApplyWatchlistTradeValueEstimate(stock);
                        _watchStocks.Insert(0, stock);
                        _watchStockByCode[stock.Code] = stock;
                        ScheduleWatchlistBasePriceRefresh(_watchStocks, TimeSpan.FromSeconds(20));
                        StartStrategyMinuteAutoPreload([stock]);
                        AppendLog($"condition enter: {stock.Name} ({stock.Code})");
                        added = true;
                    });

                    if (!added)
                        return;

                    if (added)
                    {
                        if (!IsConditionRealtimeEnterCurrent(code, generation))
                            return;

                        await TrySendConditionEnterAlertAsync(stock, "new enter", ct);
                        if (!IsConditionRealtimeEnterCurrent(code, generation))
                            return;
                        await _disclosureAlertService.TrySendRecentDisclosureAlertAsync(stock, ct);
                        if (!IsConditionRealtimeEnterCurrent(code, generation))
                            return;
                        await TrySendLateNewsAlertAsync(stock, ct);
                    }

                    await RegisterRealtime0BForCurrentWatchlistAsync();
                }
                finally
                {
                    _conditionRealtimeEnterSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"condition enter handling error: {code} / {ex.Message}"));
            }
            finally
            {
                (bool shouldRestart, long nextGeneration) = ClearConditionRealtimeEnterPending(code, generation);
                if (shouldRestart)
                    _ = ApplyConditionRealtimeAddAsync(code, name, nextGeneration);
            }
        }

        private async Task<(bool Handled, WatchStockItem? AddedStock)> ApplyConditionRealtimeAddFromCacheAsync(string code, string name, long generation)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                if (!IsConditionRealtimeEnterCurrent(code, generation))
                    return (true, (WatchStockItem?)null);

                if (_watchStockByCode.ContainsKey(code))
                    return (true, (WatchStockItem?)null);

                WatchlistStockCacheEntry? entry = GetWatchlistMemoryCache(code);
                if (entry == null)
                    return (false, (WatchStockItem?)null);

                string today = DateTime.Now.ToString("yyyyMMdd");
                if (!entry.GateBaseCandleFound ||
                    !string.Equals(entry.GateBaseCandleCheckedDate, today, StringComparison.Ordinal))
                    return (false, (WatchStockItem?)null);

                var stock = new WatchStockItem
                {
                    Code = code,
                    Name = string.IsNullOrWhiteSpace(entry.Name)
                        ? string.IsNullOrWhiteSpace(name) ? code : name
                        : entry.Name,
                    MarketTypeCode = entry.MarketTypeCode,
                    MarketName = entry.MarketName,
                    ProgramMarketType = entry.ProgramMarketType,
                    CurrentPrice = entry.CurrentPrice,
                    ChangeAmount = entry.ChangeAmount,
                    ChangeRateText = string.IsNullOrWhiteSpace(entry.ChangeRateText) ? "-" : entry.ChangeRateText,
                    VolumeText = string.IsNullOrWhiteSpace(entry.VolumeText) ? "-" : entry.VolumeText,
                    LastPrice = entry.LastPrice,
                    OrderWarning = entry.OrderWarning,
                    AuditInfo = entry.AuditInfo,
                    StockState = entry.StockState,
                    SectorName = entry.SectorName,
                    GateBaseCandleFound = entry.GateBaseCandleFound,
                    GateBaseCandleOffset = entry.GateBaseCandleOffset,
                    GateBaseCandleDate = entry.GateBaseCandleDate,
                    GateBaseCandleMarket = entry.GateBaseCandleMarket,
                    GateBaseCandleChangeRate = entry.GateBaseCandleChangeRate,
                    GateBaseCandleTradeValue = entry.GateBaseCandleTradeValue,
                    SupportsNxt = entry.SupportsNxt
                };

                ApplyWatchlistCacheToStock(stock);
                ApplyWatchlistTradeValueEstimate(stock);
                stock.PriceBrush = stock.ChangeAmount > 0 ? _upColorBrush : stock.ChangeAmount < 0 ? _downColorBrush : _whiteBrush;
                _watchStocks.Insert(0, stock);
                _watchStockByCode[stock.Code] = stock;
                ScheduleWatchlistBasePriceRefresh(_watchStocks, TimeSpan.FromSeconds(20));
                StartStrategyMinuteAutoPreload([stock]);
                AppendLog($"condition re-enter(cache): {stock.Name} ({stock.Code})");
                return (true, stock);
            });
        }

        private async Task RegisterRealtime0BForCurrentWatchlistAsync()
        {
            if (_realtimeWs != null && _realtimeWs.State == WebSocketState.Open && _realtimeCts != null)
                await RegisterRealtime0BAsync(_realtimeWs, _realtimeCts.Token);
        }

        private void ApplyConditionRealtimeRemove(string code)
        {
            MarkConditionRealtimeExit(code);
            Dispatcher.Invoke(() =>
            {
                if (!_watchStockByCode.Remove(code))
                    return;

                WatchStockItem? existing = _watchStocks.FirstOrDefault(stock => stock.Code == code);
                if (existing != null)
                    _watchStocks.Remove(existing);

                AppendLog($"condition exit: {code}");
            });

            if (_realtimeWs != null && _realtimeWs.State == WebSocketState.Open && _realtimeCts != null)
                _ = RegisterRealtime0BAsync(_realtimeWs, _realtimeCts.Token);
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

            Dispatcher.Invoke(() => AppendLog($"0s market status: 215={_lastMarketStatusCode} / {_lastMarketStatusText} / {(_isNxtMarketMode ? "use NXT" : "use KRX")}"));
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
                    StartSelectedChartRender();
                    await LoadSelectedOrderBookSnapshotAsync(selectedCode, selectionVersion);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"0s refresh re-registration error: {ex.Message}"));
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

                    AppendLog($"0D empty order book received, keep existing: {code}");
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
                HogaStatusText.Text = $"Price {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s) && s.CurrentPrice > 0 ? s.CurrentPrice.ToString("N0") : "-")} / Rate {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s2) ? s2.ChangeRateText : "-")} / 0D {_last0DReceivedAt:HH:mm:ss}";

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

                HogaStatusText.Text = $"Price {(curNum > 0 ? curNum.ToString("N0") : "-")} / Rate {FormatKrxPreviousCloseRate(curNum)} / 0D {(_last0DReceivedAt == DateTime.MinValue ? "-" : _last0DReceivedAt.ToString("HH:mm:ss"))}";
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

                AppendLog($"{source} empty order book, keep existing");
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
                _sellHogaLevels[i].IsCurrentPrice = false;
                _sellHogaLevels[i].CurrentPriceBackgroundBrush = Brushes.Transparent;
                _sellHogaLevels[i].CurrentPriceBorderBrush = Brushes.Transparent;
                _sellHogaLevels[i].CurrentPriceBorderThickness = new Thickness(0);

                _buyHogaLevels[i].PriceText = "-";
                _buyHogaLevels[i].QtyText = "-";
                _buyHogaLevels[i].RateText = string.Empty;
                _buyHogaLevels[i].RawPrice = 0;
                _buyHogaLevels[i].PriceBrush = _whiteBrush;
                _buyHogaLevels[i].RateBrush = _whiteBrush;
                _buyHogaLevels[i].IsCurrentPrice = false;
                _buyHogaLevels[i].CurrentPriceBackgroundBrush = Brushes.Transparent;
                _buyHogaLevels[i].CurrentPriceBorderBrush = Brushes.Transparent;
                _buyHogaLevels[i].CurrentPriceBorderThickness = new Thickness(0);
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
            HogaStatusText.Text = $"Price {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s) && s.CurrentPrice > 0 ? s.CurrentPrice.ToString("N0") : "-")} / Rate {(_watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? s2) ? s2.ChangeRateText : "-")} / {source} {_last0DReceivedAt:HH:mm:ss}";
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
                _sellHogaLevels[i].IsCurrentPrice = false;
                _sellHogaLevels[i].CurrentPriceBackgroundBrush = Brushes.Transparent;
                _sellHogaLevels[i].CurrentPriceBorderBrush = Brushes.Transparent;
                _sellHogaLevels[i].CurrentPriceBorderThickness = new Thickness(0);

                _buyHogaLevels[i].PriceText = "-";
                _buyHogaLevels[i].QtyText = "-";
                _buyHogaLevels[i].RateText = string.Empty;
                _buyHogaLevels[i].RawPrice = 0;
                _buyHogaLevels[i].PriceBrush = _whiteBrush;
                _buyHogaLevels[i].RateBrush = _whiteBrush;
                _buyHogaLevels[i].IsCurrentPrice = false;
                _buyHogaLevels[i].CurrentPriceBackgroundBrush = Brushes.Transparent;
                _buyHogaLevels[i].CurrentPriceBorderBrush = Brushes.Transparent;
                _buyHogaLevels[i].CurrentPriceBorderThickness = new Thickness(0);
            }

            _sellHogaLevels[9].PriceText = currentPrice.ToString("N0");
            _sellHogaLevels[9].QtyText = "MKT";
            _sellHogaLevels[9].RawPrice = currentPrice;
            _sellHogaLevels[9].PriceBrush = ResolveHogaBrushByKrxPrevClose(currentPrice);

            _buyHogaLevels[0].PriceText = currentPrice.ToString("N0");
            _buyHogaLevels[0].QtyText = "MKT";
            _buyHogaLevels[0].RawPrice = currentPrice;
            _buyHogaLevels[0].PriceBrush = ResolveHogaBrushByKrxPrevClose(currentPrice);

            _last0DReceivedAt = DateTime.Now;
            UpdateHogaSummary(null, null);
            HogaStatusText.Text = $"Price {currentPrice:N0} / Rate {(_watchStockByCode.TryGetValue(stockCode, out WatchStockItem? s) ? s.ChangeRateText : "-")} / {source} {_last0DReceivedAt:HH:mm:ss}";
            HighlightCenterPriceInHoga();
            AppendLog($"{source}: no order book, show Price/MKT fallback line: {stockCode}");
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

            ApplyRealtimeTickToStrategyMinuteLedger(code, price, volume, tradeQty, tradeTimeText);

            Dispatcher.Invoke(() =>
            {
                if (!_watchStockByCode.TryGetValue(code, out WatchStockItem? stock))
                    return;

                stock.CurrentPrice = price > 0 ? price : stock.CurrentPrice;
                UpdatePaperPositionsForPrice(code, stock.CurrentPrice);
                if (volume > 0)
                {
                    stock.VolumeText = volume.ToString("N0");
                    stock.TodayTradeValue = (long)Math.Min(long.MaxValue, stock.CurrentPrice * (double)volume);
                }

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

                ProcessStrategySignalAlerts(stock, EvaluateEnabledStrategySlots(stock));
                ProcessStrategyExitAlerts(stock);

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
                    bool keepUnifiedDailyVolume = _selectedUsesUnifiedDailyVolume && IsNxtSupportedStock(code);
                    long currentVolume = keepUnifiedDailyVolume
                        ? ParseLongAbs(_currentStatusMetrics.VolumeText)
                        : ParseLongAbs(stock.VolumeText);
                    if (!keepUnifiedDailyVolume)
                    {
                        InfoVolumeText.Text = stock.VolumeText;
                        _currentStatusMetrics.VolumeText = stock.VolumeText;
                    }
                    _currentStatusMetrics.TurnoverRateText = FormatTurnoverRate(currentVolume, ParseLongAbs(_currentStatusMetrics.ListedSharesText));
                    (string dailyVolumeRatioText, Brush dailyVolumeRatioBrush) = FormatDailyVolumeRatio(currentVolume);
                    _currentStatusMetrics.VolumeRatioText = dailyVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Text = dailyVolumeRatioText;
                    InfoPrevTimeVolumeRatioText.Foreground = dailyVolumeRatioBrush;
                    SetSelectedStockSubInfo(_currentStatusMetrics, dailyVolumeRatioText, dailyVolumeRatioBrush);
                    InfoTurnoverRateText.Text = _currentStatusMetrics.TurnoverRateText;
                    InfoTradingValueText.Text = _currentStatusMetrics.TradingValueText;
                    ApplyRealtimeChartTick(code, stock.CurrentPrice, volume > 0 ? currentVolume : 0, effectiveTradeQty, tradeTimeText);
                    UpdateStrategyProgressRows();

                }
            });
        }

        private void ApplyRealtimeTickToStrategyMinuteLedger(
            string code,
            long price,
            long cumulativeVolume,
            long fallbackTradeVolume,
            string tradeTimeText)
        {
            if (string.IsNullOrWhiteSpace(code) || price <= 0)
                return;

            bool useNxtMarket = ShouldUseNxtDataForStock(code);
            string market = useNxtMarket ? "NXT" : "KRX";
            string key = $"{NormalizeStockCode(code)}|{market}";
            long tradeVolume = 0;

            if (cumulativeVolume > 0)
            {
                if (_lastStrategyMinuteCumulativeVolumeByKey.TryGetValue(key, out long previousVolume))
                    tradeVolume = Math.Max(0, cumulativeVolume - previousVolume);

                _lastStrategyMinuteCumulativeVolumeByKey[key] = cumulativeVolume;
            }

            if (tradeVolume <= 0)
                tradeVolume = Math.Max(0, fallbackTradeVolume);

            long tradeValue = tradeVolume > 0
                ? (long)Math.Min(long.MaxValue, price * (double)tradeVolume)
                : 0;

            _strategyMinuteCacheService.ApplyRealtimeTick(
                code,
                market,
                price,
                tradeVolume,
                tradeValue,
                ParseRealtimeTradeTime(tradeTimeText));
        }

        private static DateTime ParseRealtimeTradeTime(string tradeTimeText)
        {
            string digits = new([.. (tradeTimeText ?? string.Empty).Where(char.IsDigit)]);
            DateTime now = DateTime.Now;

            if (digits.Length >= 6 &&
                int.TryParse(digits[..2], out int hour) &&
                int.TryParse(digits.Substring(2, 2), out int minute) &&
                int.TryParse(digits.Substring(4, 2), out int second) &&
                hour is >= 0 and <= 23 &&
                minute is >= 0 and <= 59 &&
                second is >= 0 and <= 59)
            {
                return new DateTime(now.Year, now.Month, now.Day, hour, minute, second);
            }

            if (digits.Length >= 4 &&
                int.TryParse(digits[..2], out hour) &&
                int.TryParse(digits.Substring(2, 2), out minute) &&
                hour is >= 0 and <= 23 &&
                minute is >= 0 and <= 59)
            {
                return new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
            }

            return now;
        }

        private void HighlightCenterPriceInHoga()
        {
            long currentPrice = ResolveSelectedCurrentPriceForHogaMarker();
            UpdateHogaRateMarkers(_sellHogaLevels, currentPrice);
            UpdateHogaRateMarkers(_buyHogaLevels, currentPrice);
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

        private long ResolveSelectedCurrentPriceForHogaMarker()
        {
            if (!string.IsNullOrWhiteSpace(_selectedStockCode)
                && _watchStockByCode.TryGetValue(_selectedStockCode, out WatchStockItem? stock)
                && stock.CurrentPrice > 0)
            {
                return stock.CurrentPrice;
            }

            return ParseLongAbs(_currentStatusMetrics.ClosePriceText);
        }

        private void UpdateHogaRateMarkers(IEnumerable<HogaLevel> levels, long currentPrice)
        {
            foreach (HogaLevel level in levels)
            {
                bool isCurrentPrice = currentPrice > 0 && level.RawPrice == currentPrice;
                level.IsCurrentPrice = isCurrentPrice;
                level.CurrentPriceBorderBrush = Brushes.Transparent;
                level.CurrentPriceBackgroundBrush = isCurrentPrice ? CreateHogaCurrentPriceBackground(level.RawPrice) : Brushes.Transparent;
                level.CurrentPriceBorderThickness = new Thickness(0);

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

        private Brush CreateHogaCurrentPriceBackground(long price)
        {
            Color color;
            if (price > 0 && _krxPrevClosePrice > 0 && price > _krxPrevClosePrice)
                color = Color.FromArgb(0x80, 0xD9, 0x21, 0x21);
            else if (price > 0 && _krxPrevClosePrice > 0 && price < _krxPrevClosePrice)
                color = Color.FromArgb(0x80, 0x12, 0x61, 0xC4);
            else
                color = Color.FromArgb(0x66, 0xB6, 0xCC, 0xD8);

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
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

            AppendLog($"0D/0H registered: {requestCode}");
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

        private readonly struct MarketStatusSnapshot(string code, string time, string expectedRemain)
        {
            public string Code { get; } = code;
            public string Time { get; } = time;
            public string ExpectedRemain { get; } = expectedRemain;
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

        private static string ResolveConditionRealtimeSeq(JsonElement root, string targetSeq)
        {
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return string.Empty;

            string normalizedTarget = NormalizeDigits(targetSeq);
            foreach (JsonElement item in data.EnumerateArray())
            {
                string seq = ReadString(item, "seq");
                if (string.IsNullOrWhiteSpace(seq) && item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                    seq = item[0].ToString();

                if (NormalizeDigits(seq) == normalizedTarget)
                    return seq.Trim();
            }

            return string.Empty;
        }

        private static string NormalizeDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string digits = new([.. value.Where(char.IsDigit)]);
            if (digits.Length == 0)
                return string.Empty;

            return int.TryParse(digits, out int number) ? number.ToString() : digits.TrimStart('0');
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
