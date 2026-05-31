using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Trading
{
    public sealed class KiwoomTradingClient
    {
        private readonly KiwoomSettings _settings;
        private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _requestGate = new(1, 1);
        private DateTime _windowStartUtc = DateTime.MinValue;
        private int _windowCount;

        private const int MaxRequestsPerSecond = 5;
        private static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(1);

        public KiwoomTradingClient(
            KiwoomSettings settings,
            Func<CancellationToken, Task<string>> accessTokenProvider,
            HttpClient? httpClient = null)
        {
            _settings = settings ?? new KiwoomSettings();
            _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
            _httpClient = httpClient ?? new HttpClient();
        }

        public event Action<string>? ApiLimitLog;

        public Task<KiwoomOrderResult> BuyAsync(KiwoomOrderRequest request, CancellationToken cancellationToken = default) =>
            SendOrderAsync("kt10000", request, cancellationToken);

        public Task<KiwoomOrderResult> SellAsync(KiwoomOrderRequest request, CancellationToken cancellationToken = default) =>
            SendOrderAsync("kt10001", request, cancellationToken);

        public async Task<KiwoomOrderResult> ModifyAsync(KiwoomModifyOrderRequest request, CancellationToken cancellationToken = default)
        {
            ValidateModifyRequest(request);

            Dictionary<string, string> body = new()
            {
                ["dmst_stex_tp"] = NormalizeOrderMarket(request.Market),
                ["orig_ord_no"] = request.OriginalOrderNo.Trim(),
                ["stk_cd"] = KiwoomTradingJson.NormalizeStockCode(request.StockCode),
                ["mdfy_qty"] = request.Quantity.ToString(),
                ["mdfy_uv"] = request.OrderPrice > 0 ? request.OrderPrice.ToString() : string.Empty,
                ["mdfy_cond_uv"] = request.ConditionPrice ?? string.Empty
            };

            return await SendOrderRequestAsync("kt10002", body, cancellationToken).ConfigureAwait(false);
        }

        public async Task<KiwoomOrderResult> CancelAsync(KiwoomCancelOrderRequest request, CancellationToken cancellationToken = default)
        {
            ValidateCancelRequest(request);

            Dictionary<string, string> body = new()
            {
                ["dmst_stex_tp"] = NormalizeOrderMarket(request.Market),
                ["orig_ord_no"] = request.OriginalOrderNo.Trim(),
                ["stk_cd"] = KiwoomTradingJson.NormalizeStockCode(request.StockCode),
                ["cncl_qty"] = request.Quantity.ToString()
            };

            return await SendOrderRequestAsync("kt10003", body, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<KiwoomOpenOrder>> GetOpenOrdersAsync(
            string stockCode = "",
            string tradeType = "0",
            string exchangeType = KiwoomTradingConstants.IntegratedExchangeType,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> body = new()
            {
                ["all_stk_tp"] = string.IsNullOrWhiteSpace(stockCode) ? "0" : "1",
                ["trde_tp"] = string.IsNullOrWhiteSpace(tradeType) ? "0" : tradeType,
                ["stk_cd"] = KiwoomTradingJson.NormalizeStockCode(stockCode),
                ["stex_tp"] = NormalizeQueryExchangeType(exchangeType)
            };

            JsonObject json = await SendAccountRequestAsync("ka10075", body, cancellationToken).ConfigureAwait(false);
            return ParseOpenOrders(json);
        }

        public async Task<IReadOnlyList<KiwoomFill>> GetFillsAsync(
            string stockCode = "",
            string orderNo = "",
            string sellType = "0",
            string exchangeType = KiwoomTradingConstants.IntegratedExchangeType,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> body = new()
            {
                ["stk_cd"] = KiwoomTradingJson.NormalizeStockCode(stockCode),
                ["qry_tp"] = string.IsNullOrWhiteSpace(stockCode) ? "0" : "1",
                ["sell_tp"] = string.IsNullOrWhiteSpace(sellType) ? "0" : sellType,
                ["ord_no"] = orderNo?.Trim() ?? string.Empty,
                ["stex_tp"] = NormalizeQueryExchangeType(exchangeType)
            };

            JsonObject json = await SendAccountRequestAsync("ka10076", body, cancellationToken).ConfigureAwait(false);
            return ParseFills(json);
        }

        public async Task<KiwoomBalanceSnapshot> GetEvaluationBalanceAsync(
            string market = KiwoomTradingConstants.MarketKrx,
            CancellationToken cancellationToken = default)
        {
            string queryMarket = NormalizeBalanceMarket(market);
            Dictionary<string, string> body = new()
            {
                ["qry_tp"] = "1",
                ["dmst_stex_tp"] = queryMarket
            };

            JsonObject json = await SendAccountRequestAsync("kt00018", body, cancellationToken).ConfigureAwait(false);
            string rawBody = json.ToJsonString();

            List<KiwoomHolding> holdings = [];
            if (json["acnt_evlt_remn_indv_tot"] is JsonArray rows)
            {
                holdings.AddRange(rows
                    .OfType<JsonObject>()
                    .Select(ParseEvaluationHolding)
                    .Where(x => !string.IsNullOrWhiteSpace(x.StockCode)));
            }

            return new KiwoomBalanceSnapshot(
                "kt00018",
                queryMarket,
                DateTime.Now,
                KiwoomTradingJson.ReadLong(json, "tot_pur_amt"),
                KiwoomTradingJson.ReadLong(json, "tot_evlt_amt"),
                KiwoomTradingJson.ReadLong(json, "tot_evlt_pl"),
                KiwoomTradingJson.ReadDecimal(json, "tot_prft_rt"),
                holdings,
                rawBody);
        }

        public async Task<KiwoomBalanceSnapshot> GetExecutionBalanceAsync(
            string market = KiwoomTradingConstants.MarketKrx,
            CancellationToken cancellationToken = default)
        {
            string queryMarket = NormalizeBalanceMarket(market);
            Dictionary<string, string> body = new()
            {
                ["dmst_stex_tp"] = queryMarket
            };

            JsonObject json = await SendAccountRequestAsync("kt00005", body, cancellationToken).ConfigureAwait(false);
            string rawBody = json.ToJsonString();

            List<KiwoomHolding> holdings = [];
            if (json["stk_cntr_remn"] is JsonArray rows)
            {
                holdings.AddRange(rows
                    .OfType<JsonObject>()
                    .Select(ParseExecutionHolding)
                    .Where(x => !string.IsNullOrWhiteSpace(x.StockCode)));
            }

            return new KiwoomBalanceSnapshot(
                "kt00005",
                queryMarket,
                DateTime.Now,
                KiwoomTradingJson.ReadLong(json, "stk_buy_tot_amt"),
                KiwoomTradingJson.ReadLong(json, "evlt_amt_tot"),
                KiwoomTradingJson.ReadLong(json, "tot_pl_tot"),
                KiwoomTradingJson.ReadDecimal(json, "tot_pl_rt"),
                holdings,
                rawBody);
        }

        public async Task<KiwoomRealizedProfitSnapshot> GetTodayRealizedProfitAsync(CancellationToken cancellationToken = default)
        {
            DateTime queryDate = ResolveRealizedProfitQueryDate(DateTime.Now);
            return await GetRealizedProfitAsync(queryDate, cancellationToken).ConfigureAwait(false);
        }

        public async Task<KiwoomRealizedProfitSnapshot> GetRealizedProfitAsync(
            DateTime queryDate,
            CancellationToken cancellationToken = default)
        {
            string dateText = queryDate.ToString("yyyyMMdd");
            Dictionary<string, string> body = new()
            {
                ["strt_dt"] = dateText,
                ["end_dt"] = dateText
            };

            JsonObject json = await SendAccountRequestAsync("ka10074", body, cancellationToken).ConfigureAwait(false);
            string rawBody = json.ToJsonString();

            long realizedProfit = KiwoomTradingJson.FindLongRecursive(
                json,
                "rlzt_pl",
                "realized_pl",
                "realizedProfit",
                "todayRealizedProfit");
            long tradeCommission = KiwoomTradingJson.FindLongRecursive(
                json,
                "trde_cmsn",
                "trade_commission");
            long tradeTax = KiwoomTradingJson.FindLongRecursive(
                json,
                "trde_tax",
                "trade_tax");

            JsonArray? rows = KiwoomTradingJson.FindArrayRecursive(
                json,
                "dt_rlzt_pl",
                "dtRlztPl",
                "realized_profit_by_date");

            if (rows is { Count: > 0 } && realizedProfit == 0)
            {
                realizedProfit = rows.OfType<JsonObject>().Sum(row => KiwoomTradingJson.FindLongRecursive(
                    row,
                    "tdy_sel_pl",
                    "rlzt_pl",
                    "todayRealizedProfit"));
                tradeCommission = rows.OfType<JsonObject>().Sum(row => KiwoomTradingJson.FindLongRecursive(
                    row,
                    "tdy_trde_cmsn",
                    "trde_cmsn"));
                tradeTax = rows.OfType<JsonObject>().Sum(row => KiwoomTradingJson.FindLongRecursive(
                    row,
                    "tdy_trde_tax",
                    "trde_tax"));
            }

            return new KiwoomRealizedProfitSnapshot(queryDate, realizedProfit, tradeCommission, tradeTax, rawBody);
        }

        private async Task<KiwoomOrderResult> SendOrderAsync(string apiId, KiwoomOrderRequest request, CancellationToken cancellationToken)
        {
            ValidateOrderRequest(request);

            Dictionary<string, string> body = new()
            {
                ["dmst_stex_tp"] = NormalizeOrderMarket(request.Market),
                ["stk_cd"] = KiwoomTradingJson.NormalizeStockCode(request.StockCode),
                ["ord_qty"] = request.Quantity.ToString(),
                ["ord_uv"] = request.OrderPrice > 0 ? request.OrderPrice.ToString() : string.Empty,
                ["trde_tp"] = string.IsNullOrWhiteSpace(request.TradeType) ? KiwoomTradingConstants.TradeTypeLimit : request.TradeType.Trim(),
                ["cond_uv"] = request.ConditionPrice ?? string.Empty
            };

            return await SendOrderRequestAsync(apiId, body, cancellationToken).ConfigureAwait(false);
        }

        private async Task<KiwoomOrderResult> SendOrderRequestAsync(string apiId, Dictionary<string, string> body, CancellationToken cancellationToken)
        {
            JsonObject json = await SendRestRequestAsync(apiId, "/api/dostk/ordr", body, cancellationToken).ConfigureAwait(false);
            string rawBody = json.ToJsonString();
            int returnCode = (int)KiwoomTradingJson.ReadLong(json, "return_code");
            string returnMessage = KiwoomTradingJson.ReadString(json, "return_msg");
            string orderNo = KiwoomTradingJson.ReadString(json, "ord_no");
            string routedMarket = KiwoomTradingJson.ReadString(json, "dmst_stex_tp");

            bool success = returnCode == 0 && !string.IsNullOrWhiteSpace(orderNo);
            return new KiwoomOrderResult(success, apiId, orderNo, routedMarket, returnCode, returnMessage, rawBody);
        }

        private async Task<JsonObject> SendAccountRequestAsync(string apiId, Dictionary<string, string> body, CancellationToken cancellationToken) =>
            await SendRestRequestAsync(apiId, "/api/dostk/acnt", body, cancellationToken).ConfigureAwait(false);

        private async Task<JsonObject> SendRestRequestAsync(
            string apiId,
            string endpoint,
            Dictionary<string, string> body,
            CancellationToken cancellationToken)
        {
            string token = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
            await WaitRestSlotAsync(cancellationToken).ConfigureAwait(false);

            string requestJson = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(endpoint));
            request.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("cont-yn", "N");
            request.Headers.TryAddWithoutValidation("next-key", string.Empty);
            request.Headers.TryAddWithoutValidation("api-id", apiId);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode == 429)
                ApiLimitLog?.Invoke($"Kiwoom trading 429: {apiId}");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Kiwoom {apiId} failed: HTTP {(int)response.StatusCode} / {response.ReasonPhrase} / {responseBody}");

            JsonNode? node = JsonNode.Parse(responseBody);
            if (node is not JsonObject json)
                throw new InvalidOperationException($"Kiwoom {apiId} returned non-object JSON.");

            return json;
        }

        private async Task WaitRestSlotAsync(CancellationToken cancellationToken)
        {
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTime now = DateTime.UtcNow;
                if (_windowStartUtc == DateTime.MinValue || now - _windowStartUtc >= RequestWindow)
                {
                    _windowStartUtc = now;
                    _windowCount = 0;
                }

                if (_windowCount >= MaxRequestsPerSecond)
                {
                    TimeSpan wait = RequestWindow - (now - _windowStartUtc);
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                    _windowStartUtc = DateTime.UtcNow;
                    _windowCount = 0;
                }

                _windowCount++;
            }
            finally
            {
                _requestGate.Release();
            }
        }

        private Uri BuildUrl(string endpoint)
        {
            string host = _settings.MockMode
                ? "https://mockapi.kiwoom.com"
                : "https://api.kiwoom.com";

            return new Uri(host + endpoint);
        }

        private static List<KiwoomOpenOrder> ParseOpenOrders(JsonObject json)
        {
            List<KiwoomOpenOrder> results = [];
            if (json["oso"] is not JsonArray rows)
                return results;

            foreach (JsonObject row in rows.OfType<JsonObject>())
            {
                results.Add(new KiwoomOpenOrder(
                    KiwoomTradingJson.ReadString(row, "ord_no"),
                    KiwoomTradingJson.NormalizeStockCode(KiwoomTradingJson.ReadString(row, "stk_cd")),
                    KiwoomTradingJson.ReadString(row, "stk_nm"),
                    KiwoomTradingJson.ReadString(row, "ord_stt"),
                    KiwoomTradingJson.ReadString(row, "io_tp_nm"),
                    KiwoomTradingJson.ReadString(row, "trde_tp"),
                    KiwoomTradingJson.ReadLong(row, "ord_qty"),
                    KiwoomTradingJson.ReadLong(row, "oso_qty"),
                    KiwoomTradingJson.ReadLong(row, "cntr_qty"),
                    KiwoomTradingJson.ReadLong(row, "ord_pric"),
                    KiwoomTradingJson.ReadLong(row, "cntr_pric"),
                    KiwoomTradingJson.ReadString(row, "stex_tp"),
                    KiwoomTradingJson.ReadString(row, "stex_tp_txt"),
                    IsSor(row)));
            }

            return results;
        }

        private static List<KiwoomFill> ParseFills(JsonObject json)
        {
            List<KiwoomFill> results = [];
            if (json["cntr"] is not JsonArray rows)
                return results;

            foreach (JsonObject row in rows.OfType<JsonObject>())
            {
                results.Add(new KiwoomFill(
                    KiwoomTradingJson.ReadString(row, "ord_no"),
                    KiwoomTradingJson.NormalizeStockCode(KiwoomTradingJson.ReadString(row, "stk_cd")),
                    KiwoomTradingJson.ReadString(row, "stk_nm"),
                    KiwoomTradingJson.ReadString(row, "ord_stt"),
                    KiwoomTradingJson.ReadString(row, "io_tp_nm"),
                    KiwoomTradingJson.ReadString(row, "trde_tp"),
                    KiwoomTradingJson.ReadLong(row, "ord_qty"),
                    KiwoomTradingJson.ReadLong(row, "cntr_qty"),
                    KiwoomTradingJson.ReadLong(row, "oso_qty"),
                    KiwoomTradingJson.ReadLong(row, "ord_pric"),
                    KiwoomTradingJson.ReadLong(row, "cntr_pric"),
                    KiwoomTradingJson.ReadString(row, "ord_tm"),
                    KiwoomTradingJson.ReadString(row, "stex_tp"),
                    KiwoomTradingJson.ReadString(row, "stex_tp_txt"),
                    IsSor(row)));
            }

            return results;
        }

        private static KiwoomHolding ParseEvaluationHolding(JsonObject row)
        {
            return new KiwoomHolding(
                KiwoomTradingJson.NormalizeStockCode(KiwoomTradingJson.ReadString(row, "stk_cd")),
                KiwoomTradingJson.ReadString(row, "stk_nm"),
                KiwoomTradingJson.ReadLong(row, "rmnd_qty"),
                KiwoomTradingJson.ReadLong(row, "trde_able_qty"),
                KiwoomTradingJson.ReadLong(row, "cur_prc"),
                KiwoomTradingJson.ReadLong(row, "pur_pric"),
                KiwoomTradingJson.ReadLong(row, "pur_amt"),
                KiwoomTradingJson.ReadLong(row, "evlt_amt"),
                KiwoomTradingJson.ReadLong(row, "evltv_prft"),
                KiwoomTradingJson.ReadDecimal(row, "prft_rt"));
        }

        private static KiwoomHolding ParseExecutionHolding(JsonObject row)
        {
            long quantity = KiwoomTradingJson.ReadLong(row, "cur_qty");
            return new KiwoomHolding(
                KiwoomTradingJson.NormalizeStockCode(KiwoomTradingJson.ReadString(row, "stk_cd")),
                KiwoomTradingJson.ReadString(row, "stk_nm"),
                quantity,
                quantity,
                KiwoomTradingJson.ReadLong(row, "cur_prc"),
                KiwoomTradingJson.ReadLong(row, "buy_uv"),
                KiwoomTradingJson.ReadLong(row, "pur_amt"),
                KiwoomTradingJson.ReadLong(row, "evlt_amt"),
                KiwoomTradingJson.ReadLong(row, "evltv_prft"),
                KiwoomTradingJson.ReadDecimal(row, "pl_rt"));
        }

        private static bool IsSor(JsonObject row)
        {
            string sorYn = KiwoomTradingJson.ReadString(row, "sor_yn");
            string exchangeText = KiwoomTradingJson.ReadString(row, "stex_tp_txt");
            return sorYn.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                   exchangeText.Equals("SOR", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeOrderMarket(string market)
        {
            string value = (market ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Contains("NXT", StringComparison.OrdinalIgnoreCase))
                return KiwoomTradingConstants.MarketSor;
            if (value.Contains("SOR", StringComparison.OrdinalIgnoreCase))
                return KiwoomTradingConstants.MarketSor;
            if (value.Contains("KRX", StringComparison.OrdinalIgnoreCase))
                return KiwoomTradingConstants.MarketKrx;

            throw new ArgumentException($"Unsupported order market: {market}");
        }

        private static string NormalizeBalanceMarket(string market)
        {
            string value = (market ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Contains("NXT", StringComparison.OrdinalIgnoreCase))
                return KiwoomTradingConstants.MarketNxt;
            return KiwoomTradingConstants.MarketKrx;
        }

        private static string NormalizeQueryExchangeType(string exchangeType)
        {
            string value = (exchangeType ?? string.Empty).Trim().ToUpperInvariant();
            return value switch
            {
                "" => KiwoomTradingConstants.IntegratedExchangeType,
                "0" => KiwoomTradingConstants.IntegratedExchangeType,
                "1" => KiwoomTradingConstants.KrxExchangeType,
                "2" => KiwoomTradingConstants.NxtExchangeType,
                "KRX" => KiwoomTradingConstants.KrxExchangeType,
                "NXT" => KiwoomTradingConstants.NxtExchangeType,
                "SOR" => KiwoomTradingConstants.IntegratedExchangeType,
                "ALL" => KiwoomTradingConstants.IntegratedExchangeType,
                "INTEGRATED" => KiwoomTradingConstants.IntegratedExchangeType,
                _ => throw new ArgumentException($"Unsupported query exchange type: {exchangeType}")
            };
        }

        private static void ValidateOrderRequest(KiwoomOrderRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateCommonOrderFields(request.StockCode, request.Quantity);
            string market = NormalizeOrderMarket(request.Market);
            if (IsSorOrNxtOrderMarket(market))
                ValidateSorNxtLimitOrder(request);
        }

        private static void ValidateModifyRequest(KiwoomModifyOrderRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateCommonOrderFields(request.StockCode, request.Quantity);
            if (string.IsNullOrWhiteSpace(request.OriginalOrderNo))
                throw new ArgumentException("Original order number is required.", nameof(request));
            string market = NormalizeOrderMarket(request.Market);
            if (IsSorOrNxtOrderMarket(market) &&
                request.OrderPrice <= 0)
                throw new ArgumentException("SOR/NXT modify orders require a positive limit price.", nameof(request));
        }

        private static void ValidateCancelRequest(KiwoomCancelOrderRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateCommonOrderFields(request.StockCode, request.Quantity);
            if (string.IsNullOrWhiteSpace(request.OriginalOrderNo))
                throw new ArgumentException("Original order number is required.", nameof(request));
            _ = NormalizeOrderMarket(request.Market);
        }

        private static void ValidateCommonOrderFields(string stockCode, long quantity)
        {
            string code = KiwoomTradingJson.NormalizeStockCode(stockCode);
            if (code.Length != 6 || code.Any(c => !char.IsDigit(c)))
                throw new ArgumentException($"Stock code must be a 6-digit code for orders: {stockCode}");

            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Order quantity must be positive.");
        }

        private static void ValidateSorNxtLimitOrder(KiwoomOrderRequest request)
        {
            string tradeType = string.IsNullOrWhiteSpace(request.TradeType)
                ? KiwoomTradingConstants.TradeTypeLimit
                : request.TradeType.Trim();

            if (tradeType == KiwoomTradingConstants.TradeTypeMarket)
                throw new ArgumentException("SOR/NXT market orders are blocked. Use a current-price tick-offset limit order.", nameof(request));

            if (tradeType != KiwoomTradingConstants.TradeTypeLimit)
                throw new ArgumentException($"SOR/NXT orders allow limit orders only. Trade type '{tradeType}' is blocked.", nameof(request));

            if (request.OrderPrice <= 0)
                throw new ArgumentException("SOR/NXT orders require a positive limit price.", nameof(request));

            if (!request.UsesTickOffsetLimit || request.ReferencePrice <= 0)
                throw new ArgumentException("SOR/NXT orders must be built from the current price with an explicit tick offset.", nameof(request));

            long expectedPrice = KiwoomOrderPriceRules.ApplyTickOffset(request.ReferencePrice, request.TickOffset);
            if (request.OrderPrice != expectedPrice)
                throw new ArgumentException($"SOR/NXT order price must match current-price tick offset. Expected {expectedPrice:N0}, got {request.OrderPrice:N0}.", nameof(request));
        }

        private static bool IsSorOrNxtOrderMarket(string market)
        {
            return string.Equals(market, KiwoomTradingConstants.MarketSor, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(market, KiwoomTradingConstants.MarketNxt, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime ResolveRealizedProfitQueryDate(DateTime now)
        {
            DateTime date = now.Date;
            bool usePreviousBusinessDay = now.TimeOfDay < new TimeSpan(8, 0, 0) ||
                                          date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            if (usePreviousBusinessDay)
                date = date.AddDays(-1);

            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                date = date.AddDays(-1);

            return date;
        }
    }
}
