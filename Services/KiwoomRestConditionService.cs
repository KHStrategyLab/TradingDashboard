using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class KiwoomRestConditionService
    {
        private readonly KiwoomSettings _settings;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _restRequestGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _restConcurrencyGate = new SemaphoreSlim(5, 5);
        private readonly SemaphoreSlim _tokenGate = new SemaphoreSlim(1, 1);
        private DateTime _lastRestRequestAtUtc = DateTime.MinValue;
        private DateTime _restBurstWindowStartUtc = DateTime.MinValue;
        private DateTime _restSustainedUntilUtc = DateTime.MinValue;
        private int _restBurstCount;
        private DateTime _restBlockedUntil = DateTime.MinValue;
        private int _restLimitErrorCount;
        private string _cachedAccessToken = string.Empty;
        private DateTime _cachedAccessTokenExpiresAt = DateTime.MinValue;

        public event Action<string>? RestLimitLog;

        private const int RestMaxCallsPerSecond = 5;
        private static readonly TimeSpan RestRateWindow = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan RestBurstInterval = TimeSpan.Zero;
        private static readonly TimeSpan RestSustainedInterval = TimeSpan.FromMilliseconds(200);
        private const bool RestUsePauseThenBurst = false;
        private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(23);
        private static readonly TimeSpan AccessTokenRefreshMargin = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RestCooldownOnLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RestMaxCooldownOnLimit = TimeSpan.FromSeconds(120);
        public KiwoomRestConditionService(KiwoomSettings settings, HttpClient? httpClient = null)
        {
            _settings = settings ?? new KiwoomSettings();
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<List<string>> GetConditionStockNamesAsync(CancellationToken cancellationToken = default)
        {
            ValidateSettings();

            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("wss://api.kiwoom.com:10000/api/dostk/websocket"), cancellationToken).ConfigureAwait(false);

            await SendJsonAsync(ws, new { trnm = "LOGIN", token }, cancellationToken).ConfigureAwait(false);
            using JsonDocument loginRes = await ReceiveByTrNameAsync(ws, "LOGIN", TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            string loginCode = ReadString(loginRes.RootElement, "return_code");
            if (loginCode != "0")
            {
                throw new InvalidOperationException($"키움 로그인 실패: code={loginCode}, msg={ReadString(loginRes.RootElement, "return_msg")}");
            }

            await SendJsonAsync(ws, new { trnm = "CNSRLST" }, cancellationToken).ConfigureAwait(false);
            using JsonDocument listRes = await ReceiveByTrNameAsync(ws, "CNSRLST", TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            string seq = ResolveConditionSeq(listRes.RootElement, _settings.ConditionSeq01);
            if (string.IsNullOrWhiteSpace(seq))
            {
                throw new InvalidOperationException($"조건식 {(_settings.ConditionSeq01 ?? "1")}번을 찾지 못했습니다.");
            }

            await SendJsonAsync(ws, new
            {
                trnm = "CNSRREQ",
                seq,
                search_type = "1",
                stex_tp = "K",
                cont_yn = "N",
                next_key = ""
            }, cancellationToken).ConfigureAwait(false);

            using JsonDocument condRes = await ReceiveByTrNameAsync(ws, "CNSRREQ", TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            return ParseConditionStockDisplayNames(condRes.RootElement);
        }

        public async Task<List<WatchStockItem>> GetConditionStocksAsync(CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);

            List<(string Code, string Name)> baseItems = await GetConditionBaseItemsAsync(token, cancellationToken).ConfigureAwait(false);
            Dictionary<string, StockMarketInfo> marketInfoByCode = await GetStockMarketInfoMapAsync(token, cancellationToken).ConfigureAwait(false);
            var result = new List<WatchStockItem>();

            foreach ((string code, string name) in baseItems)
            {
                marketInfoByCode.TryGetValue(code, out StockMarketInfo? marketInfo);
                WatchStockItem item = await GetStockInfoAsync(token, code, name, marketInfo, cancellationToken).ConfigureAwait(false);
                result.Add(item);
            }

            return result;
        }

        public async Task<List<(string Code, string Name)>> GetConditionBaseStocksAsync(CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            return await GetConditionBaseItemsAsync(token, cancellationToken).ConfigureAwait(false);
        }

        public async Task<WatchStockItem> GetConditionStockAsync(string code, string name, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string normalizedCode = NormalizeStockCode(code);
            return await GetStockInfoAsync(token, normalizedCode, name, null, cancellationToken).ConfigureAwait(false);
        }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            return IssueTokenAsync(cancellationToken);
        }

        public async Task<List<DailyCandle>> GetDailyCandlesAsync(string code, bool useNxtMarket = false, int takeCount = 240, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new List<DailyCandle>();
            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/chart");
            req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("api-id", "ka10081");
            req.Headers.TryAddWithoutValidation("cont-yn", "N");
            req.Headers.TryAddWithoutValidation("next-key", "");
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                stk_cd = requestCode,
                base_dt = System.DateTime.Now.ToString("yyyyMMdd"),
                upd_stkpc_tp = "1"
            }), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, "ka10081", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new List<DailyCandle>();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement array = FindFirstArray(doc.RootElement);
            if (array.ValueKind != JsonValueKind.Array)
                return new List<DailyCandle>();

            var candles = new List<DailyCandle>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                DailyCandle? candle = ParseDailyCandle(item);
                if (candle != null && candle.Close > 0 && !string.IsNullOrWhiteSpace(candle.Date))
                    candles.Add(candle);
            }

            return candles
                .GroupBy(c => c.Date, System.StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.Date)
                .TakeLast(takeCount)
                .ToList();
        }

        public async Task<List<DailyCandle>> GetWeeklyCandlesAsync(string code, bool useNxtMarket = false, int takeCount = 120, CancellationToken cancellationToken = default)
        {
            return await GetChartCandlesByApiIdAsync(code, useNxtMarket, "ka10082", takeCount, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<DailyCandle>> GetMinuteCandlesAsync(string code, int minute, bool useNxtMarket = false, int takeCount = 240, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new List<DailyCandle>();

            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/chart");
            req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("api-id", "ka10080");
            req.Headers.TryAddWithoutValidation("cont-yn", "N");
            req.Headers.TryAddWithoutValidation("next-key", "");
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                stk_cd = requestCode,
                tic_scope = minute.ToString(),
                upd_stkpc_tp = "1",
                base_dt = System.DateTime.Now.ToString("yyyyMMdd")
            }), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, "ka10080", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new List<DailyCandle>();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement array = FindArrayByKeySafe(doc.RootElement, "stk_min_pole_chart_qry");
            if (array.ValueKind != JsonValueKind.Array)
                array = FindFirstArray(doc.RootElement);
            if (array.ValueKind != JsonValueKind.Array)
                return new List<DailyCandle>();

            var candles = new List<DailyCandle>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                DailyCandle? candle = ParseDailyCandle(item);
                if (candle != null && candle.Close > 0 && !string.IsNullOrWhiteSpace(candle.Date))
                    candles.Add(candle);
            }

            return candles
                .GroupBy(c => c.Date, System.StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.Date)
                .TakeLast(takeCount)
                .ToList();
        }

        public async Task<List<DailyCandle>> GetMonthlyCandlesAsync(string code, bool useNxtMarket = false, int takeCount = 80, CancellationToken cancellationToken = default)
        {
            return await GetChartCandlesByApiIdAsync(code, useNxtMarket, "ka10083", takeCount, cancellationToken).ConfigureAwait(false);
        }

        private async Task<List<DailyCandle>> GetChartCandlesByApiIdAsync(string code, bool useNxtMarket, string apiId, int takeCount, CancellationToken cancellationToken)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new List<DailyCandle>();
            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/chart");
            req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("api-id", apiId);
            req.Headers.TryAddWithoutValidation("cont-yn", "N");
            req.Headers.TryAddWithoutValidation("next-key", "");
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                stk_cd = requestCode,
                base_dt = System.DateTime.Now.ToString("yyyyMMdd"),
                upd_stkpc_tp = "1"
            }), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, apiId, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new List<DailyCandle>();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement array = FindFirstArray(doc.RootElement);
            if (array.ValueKind != JsonValueKind.Array)
                return new List<DailyCandle>();

            var candles = new List<DailyCandle>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                DailyCandle? candle = ParseDailyCandle(item);
                if (candle != null && candle.Close > 0 && !string.IsNullOrWhiteSpace(candle.Date))
                    candles.Add(candle);
            }

            return candles
                .GroupBy(c => c.Date, System.StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.Date)
                .TakeLast(takeCount)
                .ToList();
        }

        public async Task<StockStatusMetrics> GetStockStatusMetricsAsync(string code, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            code = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(code))
                return new StockStatusMetrics();

            foreach (string requestCode in new[] { code, $"{code}_AL" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/stkinfo");
                req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
                req.Headers.TryAddWithoutValidation("api-id", "ka10001");
                req.Headers.TryAddWithoutValidation("cont-yn", "N");
                req.Headers.TryAddWithoutValidation("next-key", "");
                req.Content = new StringContent(JsonSerializer.Serialize(new { stk_cd = requestCode }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await SendKiwoomRestAsync(req, "ka10001-status", cancellationToken).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                long price = Math.Abs(ParseLongSafe(ReadAnyDeep(root, "cur_prc", "curPrc", "price", "now_prc", "10")));
                long open = Math.Abs(ParseLongSafe(ReadAnyDeep(root, "open_pric", "open", "stck_oprc", "시가", "16")));
                long high = Math.Abs(ParseLongSafe(ReadAnyDeep(root, "high_pric", "high", "stck_hgpr", "고가", "17")));
                long low = Math.Abs(ParseLongSafe(ReadAnyDeep(root, "low_pric", "low", "stck_lwpr", "저가", "18")));
                long volume = ParseLongSafe(ReadAnyDeep(root, "trde_qty", "trdeQty", "acc_trde_qty", "acml_vol", "volume", "13"));
                long tradingValue = ParseLongSafe(ReadAnyDeep(root, "trde_prica", "trde_amt", "acc_trde_prica", "acc_trde_amt", "acml_tr_pbmn", "14"));
                bool tradingValueFromApi = tradingValue > 0;
                long dayDiff = ParseLongSafe(ReadAnyDeep(root, "pred_pre", "predPre", "change", "chg_val", "11"));
                string changeRate = ReadAnyDeep(root, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
                string floatRatio = ReadAnyDeep(root, "distb_rt", "float_rt", "float_ratio");
                string turnoverRate = ReadAnyDeep(root, "turnover_rt", "trde_rt", "turnoverRate");
                string volumeRatio = ReadAnyDeep(root, "vol_rt", "volume_rt", "volRatio");
                long totalShares = NormalizeListedShares(ParseLongSafe(ReadAnyDeep(root, "lst_stk_cnt", "lstStkCnt", "list_stkcnt", "listed_shares", "listCount")));
                long floatShares = NormalizeListedShares(ParseLongSafe(ReadAnyDeep(root, "flo_stkcnt", "float_stkcnt", "floating_shares", "distb_stkcnt")));
                long displayShares = floatShares > 0 ? floatShares : totalShares;

                if (!string.IsNullOrWhiteSpace(changeRate) && !changeRate.Contains("%"))
                    changeRate += "%";
                if (!string.IsNullOrWhiteSpace(floatRatio) && !floatRatio.Contains("%"))
                    floatRatio += "%";
                if (!string.IsNullOrWhiteSpace(turnoverRate) && !turnoverRate.Contains("%"))
                    turnoverRate += "%";
                if (!string.IsNullOrWhiteSpace(volumeRatio) && !volumeRatio.Contains("%"))
                    volumeRatio += "%";

                if (tradingValue <= 0 && price > 0 && volume > 0)
                    tradingValue = price * volume;
                long marketCap = (price > 0 && totalShares > 0) ? price * totalShares : 0;
                string calculatedTurnoverRate = FormatTurnoverRate(volume, displayShares);
                if (string.IsNullOrWhiteSpace(turnoverRate))
                    turnoverRate = calculatedTurnoverRate;

                return new StockStatusMetrics
                {
                    OpenPriceText = open > 0 ? open.ToString("N0") : "-",
                    HighPriceText = high > 0 ? high.ToString("N0") : "-",
                    LowPriceText = low > 0 ? low.ToString("N0") : "-",
                    ClosePriceText = price > 0 ? price.ToString("N0") : "-",
                    BasePriceText = (price - dayDiff) > 0 ? (price - dayDiff).ToString("N0") : "-",
                    VolumeText = volume > 0 ? volume.ToString("N0") : "-",
                    TradingValueText = tradingValueFromApi ? FormatMillionWonUnit(tradingValue) : FormatKoreanMoney(tradingValue),
                    MarketCapText = FormatKoreanMoney(marketCap),
                    ListedSharesText = displayShares > 0 ? displayShares.ToString("N0") : "-",
                    FloatRatioText = string.IsNullOrWhiteSpace(floatRatio) ? "-" : floatRatio,
                    TurnoverRateText = string.IsNullOrWhiteSpace(turnoverRate) ? "-" : turnoverRate,
                    ChangeRateText = string.IsNullOrWhiteSpace(changeRate) ? "-" : changeRate,
                    PrevDiffText = dayDiff == 0 ? "0" : (dayDiff > 0 ? $"+{dayDiff:N0}" : $"{dayDiff:N0}"),
                    VolumeRatioText = string.IsNullOrWhiteSpace(volumeRatio) ? "-" : volumeRatio
                };
            }

            return new StockStatusMetrics();
        }

        public async Task<StockStatusMetrics> GetStockStatusMetricsByGuideAsync(string code, bool useNxtMarket = false, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new StockStatusMetrics();

            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;
            JsonElement root10100 = await PostStkInfoRootAsync(token, "ka10100", requestCode, cancellationToken).ConfigureAwait(false);
            JsonElement root10007 = await PostApiRootAsync(token, "ka10007", "/api/dostk/mrkcond", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);
            JsonElement root10095 = await PostStkInfoRootAsync(token, "ka10095", requestCode, cancellationToken).ConfigureAwait(false);
            JsonElement atn = FindFirstArrayItemSafe(root10095, "atn_stk_infr");

            long price = Math.Abs(ParseLongSafe(ReadAnyDeep(root10007, "cur_prc", "curPrc", "price", "now_prc", "10")));
            if (price <= 0)
                price = Math.Abs(ParseLongSafe(ReadAnyDeep(root10100, "lastPrice", "cur_prc", "curPrc", "price", "now_prc", "10")));
            long open = Math.Abs(ParseLongSafe(ReadAnyDeep(root10007, "open_pric", "open", "stck_oprc", "16")));
            if (open <= 0)
                open = Math.Abs(ParseLongSafe(ReadAnyDeep(root10100, "open_pric", "open", "stck_oprc", "16")));
            long high = Math.Abs(ParseLongSafe(ReadAnyDeep(root10007, "high_pric", "high", "stck_hgpr", "17")));
            if (high <= 0)
                high = Math.Abs(ParseLongSafe(ReadAnyDeep(root10100, "high_pric", "high", "stck_hgpr", "17")));
            long low = Math.Abs(ParseLongSafe(ReadAnyDeep(root10007, "low_pric", "low", "stck_lwpr", "18")));
            if (low <= 0)
                low = Math.Abs(ParseLongSafe(ReadAnyDeep(root10100, "low_pric", "low", "stck_lwpr", "18")));
            long dayDiff = ParseLongSafe(ReadAnyDeep(root10007, "pred_pre", "predPre", "change", "chg_val", "11"));
            if (dayDiff == 0)
                dayDiff = ParseLongSafe(ReadAnyDeep(root10100, "pred_pre", "predPre", "change", "chg_val", "11"));
            long basePrice = Math.Abs(ParseLongSafe(ReadAnyDeep(root10007, "base_prc", "basePrc", "std_prc", "yday_prc", "기준가")));
            if (basePrice <= 0)
                basePrice = Math.Abs(ParseLongSafe(ReadAnyDeep(root10100, "base_prc", "basePrc", "std_prc", "yday_prc", "기준가")));
            long previousClosePrice = ResolvePreviousClosePrice(root10007);
            if (previousClosePrice <= 0)
                previousClosePrice = ResolvePreviousClosePrice(root10100);
            if (previousClosePrice > 0)
                basePrice = previousClosePrice;
            if (basePrice <= 0 && price > 0 && dayDiff != 0)
                basePrice = price - dayDiff;
            if (basePrice > 0 && price > 0)
                dayDiff = price - basePrice;

            string changeRate = ReadAnyDeep(root10007, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (string.IsNullOrWhiteSpace(changeRate))
                changeRate = ReadAnyDeep(root10100, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (string.IsNullOrWhiteSpace(changeRate))
                changeRate = ReadAnyDeep(atn, "flu_rt", "chg_rt", "change_rate", "12");
            if (string.IsNullOrWhiteSpace(changeRate) && basePrice > 0 && price > 0)
                changeRate = $"{(price - basePrice) / (decimal)basePrice * 100m:0.00}";

            long volume = ParseLongSafe(ReadAnyDeep(root10007, "trde_qty", "trdeQty", "acc_trde_qty", "acml_vol", "volume", "13"));
            if (volume <= 0)
                volume = ParseLongSafe(ReadAnyDeep(atn, "trde_qty", "trdeQty", "acc_trde_qty", "acml_vol", "volume", "13"));
            long tradingValue = ParseLongSafe(ReadAnyDeep(root10007, "trde_prica", "trde_amt", "acc_trde_prica", "acc_trde_amt", "acml_tr_pbmn", "14"));
            if (tradingValue <= 0)
                tradingValue = ParseLongSafe(ReadAnyDeep(atn, "trde_prica", "trde_amt", "acc_trde_prica", "acc_trde_amt", "acml_tr_pbmn", "14"));
            bool tradingValueFromApi = tradingValue > 0;
            long marketCap = ParseLongSafe(ReadAnyDeep(atn, "mac", "market_cap"));
            bool marketCapFromApi = marketCap > 0;
            long totalShares = NormalizeListedShares(ParseLongSafe(ReadAnyDeep(root10100, "listCount", "lst_stk_cnt", "list_stkcnt", "listed_shares")));
            if (totalShares <= 0)
                totalShares = NormalizeListedShares(ParseLongSafe(ReadAnyDeep(atn, "stkcnt", "listCount", "lst_stk_cnt", "list_stkcnt", "listed_shares")));
            long floatShares = NormalizeListedShares(ParseLongSafe(ReadAnyDeep(root10007, "flo_stkcnt", "float_stkcnt", "floating_shares", "distb_stkcnt")));
            if (floatShares <= 0)
                floatShares = NormalizeListedShares(ParseLongSafe(ReadAnyDeep(atn, "flo_stkcnt", "float_stkcnt", "floating_shares", "distb_stkcnt")));
            long displayShares = floatShares > 0 ? floatShares : totalShares;

            string turnoverRate = ReadAnyDeep(atn, "turnover_rt", "trde_rt", "turnoverRate");
            string volumeRatio = ReadAnyDeep(atn, "vol_rt", "volume_rt", "volRatio");

            if (!string.IsNullOrWhiteSpace(changeRate) && !changeRate.Contains("%")) changeRate += "%";
            if (!string.IsNullOrWhiteSpace(turnoverRate) && !turnoverRate.Contains("%")) turnoverRate += "%";
            if (!string.IsNullOrWhiteSpace(volumeRatio) && !volumeRatio.Contains("%")) volumeRatio += "%";

            if (tradingValue <= 0 && price > 0 && volume > 0)
                tradingValue = price * volume;
            if (marketCap <= 0 && price > 0 && totalShares > 0)
                marketCap = price * totalShares;
            string calculatedTurnoverRate = FormatTurnoverRate(volume, displayShares);
            if (string.IsNullOrWhiteSpace(turnoverRate))
                turnoverRate = calculatedTurnoverRate;

            return new StockStatusMetrics
            {
                OpenPriceText = open > 0 ? open.ToString("N0") : "-",
                HighPriceText = high > 0 ? high.ToString("N0") : "-",
                LowPriceText = low > 0 ? low.ToString("N0") : "-",
                ClosePriceText = price > 0 ? price.ToString("N0") : "-",
                BasePriceText = basePrice > 0 ? basePrice.ToString("N0") : "-",
                VolumeText = volume > 0 ? volume.ToString("N0") : "-",
                TradingValueText = tradingValueFromApi ? FormatMillionWonUnit(tradingValue) : FormatKoreanMoney(tradingValue),
                MarketCapText = marketCapFromApi ? FormatHundredMillionWonUnit(marketCap) : FormatKoreanMoney(marketCap),
                ListedSharesText = displayShares > 0 ? displayShares.ToString("N0") : "-",
                TurnoverRateText = string.IsNullOrWhiteSpace(turnoverRate) ? "-" : turnoverRate,
                ChangeRateText = string.IsNullOrWhiteSpace(changeRate) ? "-" : changeRate,
                PrevDiffText = dayDiff == 0 ? "0" : (dayDiff > 0 ? $"+{dayDiff:N0}" : $"{dayDiff:N0}"),
                VolumeRatioText = string.IsNullOrWhiteSpace(volumeRatio) ? "-" : volumeRatio
            };
        }

        public async Task<long> GetKrxPreviousClosePriceAsync(string code, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return 0;

            JsonElement root = await PostApiRootAsync(token, "ka10007", "/api/dostk/mrkcond", new { stk_cd = baseCode }, cancellationToken).ConfigureAwait(false);
            return ResolvePreviousClosePrice(root);
        }

        public async Task<StockStatusMetrics> GetTodayExecutionSummaryAsync(string code, bool useNxtMarket = false, string programMarketType = "", CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new StockStatusMetrics();

            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;
            string today = DateTime.Now.ToString("yyyyMMdd");

            long buyCum = 0;
            long sellCum = 0;
            JsonElement root10003 = await PostApiRootAsync(token, "ka10003", "/api/dostk/stkinfo", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);
            AccumulateExecutionInfo(root10003, ref buyCum, ref sellCum, accumulateTotals: false);

            DailyTradeSummary dailyTrade = await GetUnifiedDailyTradeSummaryAsync(token, baseCode, requestCode, today, cancellationToken).ConfigureAwait(false);
            string programDate = string.IsNullOrWhiteSpace(dailyTrade.TradeDate) ? today : dailyTrade.TradeDate;
            ProgramTradeSummary programTrade = await GetProgramTradeSummaryAsync(token, baseCode, useNxtMarket, programDate, programMarketType, cancellationToken).ConfigureAwait(false);

            return new StockStatusMetrics
            {
                BuyExecCum = buyCum,
                SellExecCum = sellCum,
                DailyTradeQty = dailyTrade.TotalTradeQty,
                DailyTradeValueMillion = dailyTrade.TotalTradeValueMillion,
                TradingValueText = dailyTrade.TotalTradeValueMillion > 0 ? FormatMillionWonUnit(dailyTrade.TotalTradeValueMillion) : "-",
                BeforeMarketTradeQty = dailyTrade.BeforeMarketQty,
                RegularMarketTradeQty = dailyTrade.RegularMarketQty,
                AfterMarketTradeQty = dailyTrade.AfterMarketQty,
                DailySectionTradeQty = dailyTrade.SectionTotalQty,
                ProgramBuyText = programTrade.Found ? FormatSignedQuantity(programTrade.NetQuantity) : "-",
                ProgramNetQuantity = programTrade.NetQuantity,
                HasProgramTrade = programTrade.Found
            };
        }

        public async Task<IReadOnlyList<(string Date, long Volume)>> GetUnifiedDailyTradeVolumesAsync(string code, int takeCount = 5, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return Array.Empty<(string Date, long Volume)>();

            string today = DateTime.Now.ToString("yyyyMMdd");
            JsonElement root = await PostApiRootAsync(token, "ka10015", "/api/dostk/stkinfo", new { stk_cd = $"{baseCode}_AL", strt_dt = today }, cancellationToken).ConfigureAwait(false);
            return ReadDailyTradeVolumeRows(root)
                .Where(r => r.Volume > 0)
                .OrderByDescending(r => r.Date)
                .Take(Math.Max(1, takeCount))
                .ToList();
        }

        public async Task<KrxClosingSnapshot> GetKrxClosingSnapshotAsync(string code, bool useNxtMarket = false, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            var snapshot = new KrxClosingSnapshot { Code = baseCode };
            if (string.IsNullOrWhiteSpace(baseCode))
                return snapshot;

            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;
            JsonElement quoteRoot = await PostApiRootAsync(token, "ka10007", "/api/dostk/mrkcond", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);
            JsonElement hogaRoot = await PostApiRootAsync(token, "ka10004", "/api/dostk/mrkcond", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);

            snapshot.CurrentPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(quoteRoot, "cur_prc", "curPrc", "now_prc", "price", "10")));
            snapshot.DayChange = ParseLongSafe(ReadAnyDeep(quoteRoot, "pred_pre", "predPre", "change", "chg_val", "11"));
            string rate = ReadAnyDeep(quoteRoot, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (!string.IsNullOrWhiteSpace(rate) && !rate.Contains("%"))
                rate += "%";
            snapshot.ChangeRateText = string.IsNullOrWhiteSpace(rate) ? "-" : rate;

            long basePrice = Math.Abs(ParseLongSafe(ReadAnyDeep(quoteRoot, "base_prc", "basePrc", "std_prc", "yday_prc", "기준가")));
            long previousClosePrice = ResolvePreviousClosePrice(quoteRoot);
            if (previousClosePrice > 0)
                basePrice = previousClosePrice;
            if (basePrice <= 0 && snapshot.CurrentPrice > 0 && snapshot.DayChange != 0)
                basePrice = snapshot.CurrentPrice - snapshot.DayChange;
            snapshot.BasePrice = Math.Max(0, basePrice);
            if (snapshot.BasePrice > 0 && snapshot.CurrentPrice > 0)
                snapshot.DayChange = snapshot.CurrentPrice - snapshot.BasePrice;

            for (int i = 0; i < 10; i++)
            {
                int level = i + 1;
                long sellPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (41 + i).ToString(), level == 1 ? "sel_fpr_bid" : string.Empty, $"sel_{level}bid", $"sel_{level}_bid", $"sel_{level}th_pre_bid", $"sell_{level}_price", $"ask_{level}_price")));
                long sellQty = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (61 + i).ToString(), level == 1 ? "sel_fpr_req" : string.Empty, $"sel_{level}bid_req", $"sel_{level}_req", $"sel_{level}th_pre_req", $"sell_{level}_qty", $"ask_{level}_qty")));
                snapshot.SellLevels.Add(new HogaQuoteLevel { Price = sellPrice, Quantity = sellQty });

                long buyPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (51 + i).ToString(), level == 1 ? "buy_fpr_bid" : string.Empty, $"buy_{level}bid", $"buy_{level}_bid", $"buy_{level}th_pre_bid", $"buy_{level}_price", $"bid_{level}_price")));
                long buyQty = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (71 + i).ToString(), level == 1 ? "buy_fpr_req" : string.Empty, $"buy_{level}bid_req", $"buy_{level}_req", $"buy_{level}th_pre_req", $"buy_{level}_qty", $"bid_{level}_qty")));
                snapshot.BuyLevels.Add(new HogaQuoteLevel { Price = buyPrice, Quantity = buyQty });
            }

            JsonElement execInfoRoot = await PostApiRootAsync(token, "ka10003", "/api/dostk/stkinfo", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);
            long snapshotBuyCum = 0;
            long snapshotSellCum = 0;
            AccumulateExecutionInfo(execInfoRoot, ref snapshotBuyCum, ref snapshotSellCum, snapshot.RecentTrades, accumulateTotals: false);
            snapshot.BuyExecCum = snapshotBuyCum;
            snapshot.SellExecCum = snapshotSellCum;

            string snapshotDate = DateTime.Now.ToString("yyyyMMdd");
            DailyTradeSummary snapshotDailyTrade = await GetUnifiedDailyTradeSummaryAsync(token, baseCode, requestCode, snapshotDate, cancellationToken).ConfigureAwait(false);
            snapshot.DailyTradeQty = snapshotDailyTrade.TotalTradeQty;
            snapshot.DailyTradeValueMillion = snapshotDailyTrade.TotalTradeValueMillion;
            snapshot.BeforeMarketTradeQty = snapshotDailyTrade.BeforeMarketQty;
            snapshot.RegularMarketTradeQty = snapshotDailyTrade.RegularMarketQty;
            snapshot.AfterMarketTradeQty = snapshotDailyTrade.AfterMarketQty;
            snapshot.DailySectionTradeQty = snapshotDailyTrade.SectionTotalQty;

            return snapshot;
        }

        private async Task<ProgramTradeSummary> GetProgramTradeSummaryAsync(string token, string baseCode, bool useNxtMarket, string date, string programMarketType, CancellationToken cancellationToken)
        {
            string mtsRequestCode = $"{baseCode}_AL";
            JsonElement mtsRoot = await PostApiRootAsync(
                token,
                "ka90008",
                "/api/dostk/mrkcond",
                new { amt_qty_tp = "2", stk_cd = mtsRequestCode, date },
                cancellationToken).ConfigureAwait(false);

            ProgramTradeSummary mtsSummary = ReadProgramTimeSummary(mtsRoot, date);
            if (mtsSummary.Found)
                return mtsSummary;

            return new ProgramTradeSummary(false, 0, string.Empty);

        }

        private async Task<DailyTradeSummary> GetUnifiedDailyTradeSummaryAsync(string token, string baseCode, string fallbackRequestCode, string date, CancellationToken cancellationToken)
        {
            string unifiedCode = $"{baseCode}_AL";
            if (!string.Equals(unifiedCode, fallbackRequestCode, StringComparison.OrdinalIgnoreCase))
            {
                JsonElement unifiedRoot = await PostApiRootAsync(token, "ka10015", "/api/dostk/stkinfo", new { stk_cd = unifiedCode, strt_dt = date }, cancellationToken).ConfigureAwait(false);
                DailyTradeSummary unified = ReadDailyTradeSummary(unifiedRoot);
                if (unified.TotalTradeQty > 0 || unified.SectionTotalQty > 0 || unified.TotalTradeValueMillion > 0)
                    return unified;
            }

            JsonElement fallbackRoot = await PostApiRootAsync(token, "ka10015", "/api/dostk/stkinfo", new { stk_cd = fallbackRequestCode, strt_dt = date }, cancellationToken).ConfigureAwait(false);
            return ReadDailyTradeSummary(fallbackRoot);
        }
        private static ProgramTradeSummary ReadProgramTimeSummary(JsonElement root, string date)
        {
            JsonElement rows = FindArrayByKeySafe(root, "stk_tm_prm_trde_trnsn");
            if (rows.ValueKind != JsonValueKind.Array)
                return new ProgramTradeSummary(false, 0, string.Empty);

            foreach (JsonElement row in rows.EnumerateArray())
            {
                string rowDate = NormalizeDigits(ReadAnyDeep(row, "dt", "date"));
                if (!string.IsNullOrWhiteSpace(rowDate) && !string.Equals(rowDate, date, StringComparison.Ordinal))
                    continue;

                return ReadProgramTradeRow(row);
            }

            return new ProgramTradeSummary(false, 0, string.Empty);
        }
        private static ProgramTradeSummary ReadProgramTradeRow(JsonElement row)
        {
            long sellQuantity = ParseLongSafe(ReadAnyDeep(row, "prm_sell_qty", "prmSellQty"));
            long buyQuantity = ParseLongSafe(ReadAnyDeep(row, "prm_buy_qty", "prmBuyQty"));
            long netQuantity = ParseLongSafe(ReadAnyDeep(row, "prm_netprps_amt", "prmNetprpsAmt"));
            if (netQuantity == 0 && (buyQuantity != 0 || sellQuantity != 0))
                netQuantity = ParseLongSafe(ReadAnyDeep(row, "prm_buy_amt", "prmBuyAmt")) - ParseLongSafe(ReadAnyDeep(row, "prm_sell_amt", "prmSellAmt"));

            string logText = $"tm={ReadAnyDeep(row, "tm", "time")} stex={ReadAnyDeep(row, "stex_tp", "stexTp")} sellQty={sellQuantity:N0} buyQty={buyQuantity:N0} netQty={netQuantity:N0} sellAmt={ReadAnyDeep(row, "prm_sell_amt", "prmSellAmt")} buyAmt={ReadAnyDeep(row, "prm_buy_amt", "prmBuyAmt")} netAmt={ReadAnyDeep(row, "prm_netprps_amt", "prmNetprpsAmt")}";
            return new ProgramTradeSummary(true, netQuantity, logText);
        }
        public async Task<KrxClosingSnapshot> GetOrderBookSnapshotAsync(string code, bool useNxtMarket = false, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            var snapshot = new KrxClosingSnapshot { Code = baseCode };
            if (string.IsNullOrWhiteSpace(baseCode))
                return snapshot;

            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;
            JsonElement hogaRoot = await PostApiRootAsync(token, "ka10004", "/api/dostk/mrkcond", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < 10; i++)
            {
                int level = i + 1;
                long sellPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (41 + i).ToString(), level == 1 ? "sel_fpr_bid" : string.Empty, $"sel_{level}bid", $"sel_{level}_bid", $"sel_{level}th_pre_bid", $"sell_{level}_price", $"ask_{level}_price")));
                long sellQty = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (61 + i).ToString(), level == 1 ? "sel_fpr_req" : string.Empty, $"sel_{level}bid_req", $"sel_{level}_req", $"sel_{level}th_pre_req", $"sell_{level}_qty", $"ask_{level}_qty")));
                snapshot.SellLevels.Add(new HogaQuoteLevel { Price = sellPrice, Quantity = sellQty });

                long buyPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (51 + i).ToString(), level == 1 ? "buy_fpr_bid" : string.Empty, $"buy_{level}bid", $"buy_{level}_bid", $"buy_{level}th_pre_bid", $"buy_{level}_price", $"bid_{level}_price")));
                long buyQty = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (71 + i).ToString(), level == 1 ? "buy_fpr_req" : string.Empty, $"buy_{level}bid_req", $"buy_{level}_req", $"buy_{level}th_pre_req", $"buy_{level}_qty", $"bid_{level}_qty")));
                snapshot.BuyLevels.Add(new HogaQuoteLevel { Price = buyPrice, Quantity = buyQty });
            }

            return snapshot;
        }

        private async Task<JsonElement> PostStkInfoRootAsync(string token, string apiId, string requestCode, CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/stkinfo");
            req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("api-id", apiId);
            req.Headers.TryAddWithoutValidation("cont-yn", "N");
            req.Headers.TryAddWithoutValidation("next-key", "");
            req.Content = new StringContent(JsonSerializer.Serialize(new { stk_cd = requestCode }), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, apiId, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                return JsonDocument.Parse("{}").RootElement;

            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private async Task<JsonElement> PostApiRootAsync(string token, string apiId, string endpoint, object body, CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.kiwoom.com{endpoint}");
            req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("api-id", apiId);
            req.Headers.TryAddWithoutValidation("cont-yn", "N");
            req.Headers.TryAddWithoutValidation("next-key", "");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, apiId, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                return JsonDocument.Parse("{}").RootElement;

            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private async IAsyncEnumerable<JsonElement> PostApiRootPagesAsync(string token, string apiId, string endpoint, object body, int maxPages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string contYn = "N";
            string nextKey = string.Empty;

            for (int page = 0; page < maxPages; page++)
            {
                ApiPageResult result = await PostApiPageAsync(token, apiId, endpoint, body, contYn, nextKey, cancellationToken).ConfigureAwait(false);
                yield return result.Root;

                if (!IsContinuation(result.ContYn) || string.IsNullOrWhiteSpace(result.NextKey))
                    yield break;

                contYn = "Y";
                nextKey = result.NextKey;
            }
        }

        private async Task<ApiPageResult> PostApiPageAsync(string token, string apiId, string endpoint, object body, string contYn, string nextKey, CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.kiwoom.com{endpoint}");
            req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("api-id", apiId);
            req.Headers.TryAddWithoutValidation("cont-yn", contYn);
            req.Headers.TryAddWithoutValidation("next-key", nextKey);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, apiId, cancellationToken).ConfigureAwait(false);
            string responseContYn = ReadHeader(response, "cont-yn");
            string responseNextKey = ReadHeader(response, "next-key");
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                return new ApiPageResult(JsonDocument.Parse("{}").RootElement.Clone(), responseContYn, responseNextKey);

            using JsonDocument doc = JsonDocument.Parse(json);
            return new ApiPageResult(doc.RootElement.Clone(), responseContYn, responseNextKey);
        }

        private static void AccumulateExecutionInfo(JsonElement root, ref long buyCum, ref long sellCum, List<ClosingTradePrint>? recentTrades = null, bool accumulateTotals = true)
        {
            JsonElement execArray = FindArrayByKeySafe(root, "cntr_infr");
            if (execArray.ValueKind != JsonValueKind.Array)
                execArray = FindArrayByKeySafe(root, "cntr_info");
            if (execArray.ValueKind != JsonValueKind.Array)
                execArray = FindArrayByKeySafe(root, "execution_info");

            if (execArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in execArray.EnumerateArray())
                    AccumulateExecutionRow(row, ref buyCum, ref sellCum, recentTrades, accumulateTotals);
                return;
            }

            AccumulateExecutionRow(root, ref buyCum, ref sellCum, recentTrades, accumulateTotals);
        }

        private static void AccumulateExecutionRow(JsonElement row, ref long buyCum, ref long sellCum, List<ClosingTradePrint>? recentTrades, bool accumulateTotals)
        {
            string qtyText = ReadAnyDeep(row, "cntr_trde_qty", "cntr_qty", "trade_qty", "trde_qty", "15");
            long signedQty = ParseLongSafe(qtyText);
            long qty = Math.Abs(signedQty);
            if (qty <= 0)
                return;

            int side = ResolveExecutionSide(row, qtyText);
            if (side == 0)
                return;

            if (accumulateTotals)
            {
                if (side > 0)
                    buyCum += qty;
                else
                    sellCum += qty;
            }

            if (recentTrades == null || recentTrades.Count >= 10)
                return;

            long tradePrice = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "cntr_pric", "cntr_price", "cur_prc", "trade_price", "10")));
            recentTrades.Add(new ClosingTradePrint
            {
                Price = tradePrice,
                Quantity = qty,
                IsBuyAggressive = side > 0
            });
        }

        private static int ResolveExecutionSide(JsonElement row, string qtyText)
        {
            string quantity = (qtyText ?? string.Empty).Trim();
            if (quantity.StartsWith("+", StringComparison.Ordinal))
                return 1;
            if (quantity.StartsWith("-", StringComparison.Ordinal))
                return -1;

            string sideText = ReadAnyDeep(row, "cntr_sign", "trade_sign", "cntr_tp", "trade_tp", "ms_md_tp", "buy_sell_tp", "매수매도구분");
            if (string.IsNullOrWhiteSpace(sideText))
                return 0;

            string normalized = sideText.Trim().ToUpperInvariant();
            if (normalized.Contains("BUY") || normalized.Contains("매수") || normalized == "+" || normalized == "B")
                return 1;
            if (normalized.Contains("SELL") || normalized.Contains("매도") || normalized == "-" || normalized == "S")
                return -1;

            return 0;
        }

        private static DailyTradeSummary ReadDailyTradeSummary(JsonElement root)
        {
            var summary = new DailyTradeSummary();
            JsonElement dailyArray = FindArrayByKeySafe(root, "daly_trde_dtl");
            if (dailyArray.ValueKind != JsonValueKind.Array)
                dailyArray = FindArrayByKeySafe(root, "daily_trade_detail");
            if (dailyArray.ValueKind != JsonValueKind.Array)
                dailyArray = FindArrayByKeySafe(root, "daily_transaction_detail");

            if (dailyArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in dailyArray.EnumerateArray())
                {
                    ReadDailyTransactionRow(row, ref summary);
                    if (!string.IsNullOrWhiteSpace(summary.TradeDate) || summary.TotalTradeQty > 0 || summary.SectionTotalQty > 0)
                        return summary;
                }
                return summary;
            }

            ReadDailyTransactionRow(root, ref summary);
            return summary;
        }

        private static List<(string Date, long Volume)> ReadDailyTradeVolumeRows(JsonElement root)
        {
            var rows = new List<(string Date, long Volume)>();
            JsonElement dailyArray = FindArrayByKeySafe(root, "daly_trde_dtl");
            if (dailyArray.ValueKind != JsonValueKind.Array)
                dailyArray = FindArrayByKeySafe(root, "daily_trade_detail");
            if (dailyArray.ValueKind != JsonValueKind.Array)
                dailyArray = FindArrayByKeySafe(root, "daily_transaction_detail");

            if (dailyArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in dailyArray.EnumerateArray())
                    AddDailyTradeVolumeRow(row, rows);
            }
            else
            {
                AddDailyTradeVolumeRow(root, rows);
            }

            return rows;
        }

        private static void AddDailyTradeVolumeRow(JsonElement row, List<(string Date, long Volume)> rows)
        {
            string date = NormalizeDigits(ReadAnyDeep(row, "dt", "date", "trde_dt", "base_dt"));
            long volume = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "trde_qty", "total_trde_qty", "trade_qty", "volume")));
            if (volume <= 0)
                volume = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "tot_3", "section_total_qty")));
            if (!string.IsNullOrWhiteSpace(date) && volume > 0)
                rows.Add((date, volume));
        }

        private static void ReadDailyTransactionRow(JsonElement row, ref DailyTradeSummary summary)
        {
            summary.TradeDate = NormalizeDigits(ReadAnyDeep(row, "dt", "date", "trde_dt", "base_dt"));
            summary.TotalTradeQty = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "trde_qty", "total_trde_qty", "trade_qty", "volume")));
            summary.TotalTradeValueMillion = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "trde_prica", "total_trde_prica", "trade_value", "trading_value")));
            summary.BeforeMarketQty = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "bf_mkrt_trde_qty", "before_market_qty")));
            summary.RegularMarketQty = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "opmr_trde_qty", "regular_market_qty", "open_market_qty")));
            summary.AfterMarketQty = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "af_mkrt_trde_qty", "after_market_qty")));
            summary.BeforeMarketValueMillion = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "bf_mkrt_trde_prica", "before_market_trde_prica")));
            summary.RegularMarketValueMillion = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "opmr_trde_prica", "regular_market_trde_prica", "open_market_trde_prica")));
            summary.AfterMarketValueMillion = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "af_mkrt_trde_prica", "after_market_trde_prica")));
            summary.SectionTotalQty = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "tot_3", "section_total_qty")));
            summary.PeriodTradeQty = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "prid_trde_qty", "period_trade_qty")));
            if (summary.TotalTradeValueMillion <= 0)
                summary.TotalTradeValueMillion = summary.BeforeMarketValueMillion + summary.RegularMarketValueMillion + summary.AfterMarketValueMillion;
        }

        private struct DailyTradeSummary
        {
            public string TradeDate;
            public long TotalTradeQty;
            public long TotalTradeValueMillion;
            public long BeforeMarketQty;
            public long RegularMarketQty;
            public long AfterMarketQty;
            public long BeforeMarketValueMillion;
            public long RegularMarketValueMillion;
            public long AfterMarketValueMillion;
            public long SectionTotalQty;
            public long PeriodTradeQty;
        }

        private readonly record struct ProgramTradeSummary(bool Found, long NetQuantity, string LogText);

        private static string FormatSignedQuantity(long value)
        {
            if (value == 0)
                return "0";
            return value > 0 ? $"+{value:N0}" : value.ToString("N0");
        }

        private static string FormatTurnoverRate(long volume, long listedShares)
        {
            if (volume <= 0 || listedShares <= 0)
                return string.Empty;

            decimal rate = volume / (decimal)listedShares * 100m;
            decimal displayRate = Math.Truncate(rate * 100m) / 100m;
            return $"{displayRate:0.00}%";
        }

        private static long NormalizeListedShares(long value)
        {
            if (value <= 0)
                return 0;

            // Some Kiwoom TRs return listed shares in 1,000-share units while ka10099 listCount is 1-share based.
            // Very small raw values like 10,395 for Samhwa Capacitor should be shown as 10,395,000 shares.
            return value < 1_000_000 ? value * 1000 : value;
        }

        private static bool IsContinuation(string value)
        {
            return string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "y", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadHeader(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
                return values.FirstOrDefault() ?? string.Empty;
            if (response.Content.Headers.TryGetValues(name, out IEnumerable<string>? contentValues))
                return contentValues.FirstOrDefault() ?? string.Empty;
            return string.Empty;
        }

        private readonly record struct ApiPageResult(JsonElement Root, string ContYn, string NextKey);

        private static JsonElement FindArrayByKeySafe(JsonElement root, string key)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                return arr;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(key, out JsonElement dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                return dataArr;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data2) && data2.ValueKind == JsonValueKind.Array)
                return data2;
            return root;
        }

        private static JsonElement FindFirstArrayItemSafe(JsonElement root, string key)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out JsonElement arr) &&
                arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                return arr[0];

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) &&
                data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                JsonElement first = data[0];
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty(key, out JsonElement nested) &&
                    nested.ValueKind == JsonValueKind.Array && nested.GetArrayLength() > 0)
                    return nested[0];
                return first;
            }

            return root;
        }

        private void ValidateSettings()
        {
            if (string.IsNullOrWhiteSpace(_settings.AppKey) || string.IsNullOrWhiteSpace(_settings.SecretKey))
            {
                throw new InvalidOperationException("키움 AppKey/SecretKey가 설정되지 않았습니다.");
            }
        }

        private async Task<string> IssueTokenAsync(CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && now < _cachedAccessTokenExpiresAt - AccessTokenRefreshMargin)
                return _cachedAccessToken;

            await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                now = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && now < _cachedAccessTokenExpiresAt - AccessTokenRefreshMargin)
                    return _cachedAccessToken;

                string token = await RequestNewTokenAsync(cancellationToken).ConfigureAwait(false);
                _cachedAccessToken = token;
                _cachedAccessTokenExpiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
                return token;
            }
            finally
            {
                _tokenGate.Release();
            }
        }

        private async Task<string> RequestNewTokenAsync(CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/oauth2/token");
            string payload = JsonSerializer.Serialize(new
            {
                grant_type = "client_credentials",
                appkey = _settings.AppKey,
                secretkey = _settings.SecretKey
            });
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await SendKiwoomRestAsync(req, "oauth2-token", cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(json);
            string token = ReadString(doc.RootElement, "token");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("키움 토큰 응답에 token 값이 없습니다.");

            return token;
        }

        private async Task WaitRestRateLimitAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                TimeSpan wait;
                await _restRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    DateTime now = DateTime.UtcNow;
                    bool sustainedMode = now < _restSustainedUntilUtc;
                    TimeSpan minInterval = sustainedMode ? RestSustainedInterval : RestBurstInterval;
                    TimeSpan intervalWait = TimeSpan.Zero;
                    if (_lastRestRequestAtUtc != DateTime.MinValue && now - _lastRestRequestAtUtc < minInterval)
                        intervalWait = minInterval - (now - _lastRestRequestAtUtc);

                    if (sustainedMode)
                    {
                        DateTime scheduledAt = now + intervalWait;
                        _lastRestRequestAtUtc = scheduledAt;
                        wait = intervalWait;
                    }
                    else if (_restBurstWindowStartUtc == DateTime.MinValue
                        || now - _restBurstWindowStartUtc >= RestRateWindow)
                    {
                        _restBurstWindowStartUtc = now;
                        _restBurstCount = 1;

                        DateTime scheduledAt = now + intervalWait;
                        _lastRestRequestAtUtc = scheduledAt;
                        wait = intervalWait;
                    }
                    else if (_restBurstCount < RestMaxCallsPerSecond)
                    {
                        _restBurstCount++;

                        DateTime scheduledAt = now + intervalWait;
                        _lastRestRequestAtUtc = scheduledAt;
                        wait = intervalWait;
                    }
                    else
                    {
                        TimeSpan windowWait = RestRateWindow - (now - _restBurstWindowStartUtc);
                        wait = windowWait > intervalWait ? windowWait : intervalWait;
                        _restSustainedUntilUtc = RestUsePauseThenBurst ? DateTime.MinValue : now + wait + RestRateWindow;
                        _restBurstWindowStartUtc = RestUsePauseThenBurst ? DateTime.MinValue : _restSustainedUntilUtc;
                        _restBurstCount = 0;
                    }
                }
                finally
                {
                    _restRequestGate.Release();
                }

                if (wait.TotalMilliseconds <= 0)
                    return;

                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }

        private TimeSpan ApplyRestLimitCooldown()
        {
            _restLimitErrorCount++;
            double cooldownSeconds = Math.Min(
                RestCooldownOnLimit.TotalSeconds * Math.Max(1, _restLimitErrorCount),
                RestMaxCooldownOnLimit.TotalSeconds);
            _restBlockedUntil = DateTime.Now.AddSeconds(cooldownSeconds);
            return TimeSpan.FromSeconds(cooldownSeconds);
        }

        private static bool IsKiwoomLimitReturnCode(string code)
        {
            return code == "1700" || code == "1701" || code == "1702" || code == "1687";
        }

        private async Task<HttpResponseMessage> SendKiwoomRestAsync(HttpRequestMessage request, string tag, CancellationToken cancellationToken)
        {
            DateTime now = DateTime.Now;
            if (now < _restBlockedUntil)
            {
                TimeSpan wait = _restBlockedUntil - now;
                if (wait.TotalMilliseconds > 0)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            await WaitRestRateLimitAsync(cancellationToken).ConfigureAwait(false);
            await _restConcurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string body = string.Empty;

                try
                {
                    body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response.Content?.Dispose();
                    response.Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json");
                }
                catch
                {
                    body = string.Empty;
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    TimeSpan cooldown = ApplyRestLimitCooldown();
                    RestLimitLog?.Invoke($"Kiwoom REST 429: {tag} / wait {cooldown.TotalSeconds:0}s");
                    return response;
                }

                if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(body);
                        string returnCode = ReadAny(doc.RootElement, "return_code", "rt_cd", "returnCode");
                        if (IsKiwoomLimitReturnCode(returnCode))
                            ApplyRestLimitCooldown();
                        else if (response.IsSuccessStatusCode && _restLimitErrorCount > 0)
                            _restLimitErrorCount = 0;
                    }
                    catch
                    {
                        if (response.IsSuccessStatusCode && _restLimitErrorCount > 0)
                            _restLimitErrorCount = 0;
                    }
                }
                else if (response.IsSuccessStatusCode && _restLimitErrorCount > 0)
                {
                    _restLimitErrorCount = 0;
                }

                return response;
            }
            finally
            {
                _restConcurrencyGate.Release();
            }
        }

        private static async Task SendJsonAsync(ClientWebSocket ws, object payload, CancellationToken cancellationToken)
        {
            string text = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<JsonDocument> ReceiveByTrNameAsync(ClientWebSocket ws, string targetTrName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            while (true)
            {
                string text = await ReceiveTextAsync(ws, cts.Token).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(text);
                string trName = ReadString(doc.RootElement, "trnm");

                if (trName == "PING")
                {
                    byte[] pingBytes = Encoding.UTF8.GetBytes(text);
                    await ws.SendAsync(pingBytes, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(trName, targetTrName, StringComparison.OrdinalIgnoreCase))
                {
                    return JsonDocument.Parse(text);
                }
            }
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("키움 WebSocket 연결이 종료되었습니다.");

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    break;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private async Task<List<(string Code, string Name)>> GetConditionBaseItemsAsync(string token, CancellationToken cancellationToken)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("wss://api.kiwoom.com:10000/api/dostk/websocket"), cancellationToken).ConfigureAwait(false);

            await SendJsonAsync(ws, new { trnm = "LOGIN", token }, cancellationToken).ConfigureAwait(false);
            using JsonDocument loginRes = await ReceiveByTrNameAsync(ws, "LOGIN", TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            string loginCode = ReadString(loginRes.RootElement, "return_code");
            if (loginCode != "0")
                throw new InvalidOperationException($"키움 로그인 실패: code={loginCode}, msg={ReadString(loginRes.RootElement, "return_msg")}");

            await SendJsonAsync(ws, new { trnm = "CNSRLST" }, cancellationToken).ConfigureAwait(false);
            using JsonDocument listRes = await ReceiveByTrNameAsync(ws, "CNSRLST", TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            string seq = ResolveConditionSeq(listRes.RootElement, _settings.ConditionSeq01);
            if (string.IsNullOrWhiteSpace(seq))
                throw new InvalidOperationException($"조건식 {(_settings.ConditionSeq01 ?? "1")}번을 찾지 못했습니다.");

            await SendJsonAsync(ws, new { trnm = "CNSRREQ", seq, search_type = "1", stex_tp = "K", cont_yn = "N", next_key = "" }, cancellationToken).ConfigureAwait(false);
            using JsonDocument condRes = await ReceiveByTrNameAsync(ws, "CNSRREQ", TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            return ParseConditionBaseItems(condRes.RootElement);
        }

        private async Task<WatchStockItem> GetStockInfoAsync(string token, string code, string name, StockMarketInfo? marketInfo, CancellationToken cancellationToken)
        {
            var fallback = new WatchStockItem
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(name) ? code : name,
                VolumeText = "-",
                ChangeRateText = "-",
                MarketTypeCode = marketInfo?.MarketTypeCode ?? string.Empty,
                MarketName = marketInfo?.MarketName ?? string.Empty,
                ProgramMarketType = marketInfo?.ProgramMarketType ?? string.Empty,
                LastPrice = marketInfo?.LastPrice ?? 0,
                OrderWarning = marketInfo?.OrderWarning ?? string.Empty,
                AuditInfo = marketInfo?.AuditInfo ?? string.Empty,
                StockState = marketInfo?.StockState ?? string.Empty,
                SectorName = marketInfo?.SectorName ?? string.Empty,
                SupportsNxt = false
            };

            if (string.IsNullOrWhiteSpace(code))
                return fallback;

            WatchStockItem? krxItem = null;
            WatchStockItem? nxtItem = null;

            foreach (string requestCode in new[] { code, $"{code}_NX" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/stkinfo");
                req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
                req.Headers.TryAddWithoutValidation("api-id", "ka10001");
                req.Headers.TryAddWithoutValidation("cont-yn", "N");
                req.Headers.TryAddWithoutValidation("next-key", "");
                req.Content = new StringContent(JsonSerializer.Serialize(new { stk_cd = requestCode }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await SendKiwoomRestAsync(req, "ka10001-watch", cancellationToken).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string itemName = ReadAnyDeep(root, "stk_nm", "stkNm", "name", "hname", "item_name");
            long price = ParseLongSafe(ReadAnyDeep(root, "cur_prc", "curPrc", "price", "now_prc"));
            long volume = ParseLongSafe(ReadAnyDeep(root, "trde_qty", "trdeQty", "acc_trde_qty", "acml_vol", "volume"));
            long dayChange = ParseLongSafe(ReadAnyDeep(root, "pred_pre", "predPre", "change", "chg_val", "11"));
            long prev = ParseLongSafe(ReadAnyDeep(root, "base_prc", "basePrc", "yday_prc"));
            string rateText = ReadAnyDeep(root, "flu_rt", "fluRt", "chg_rt", "change_rate");
                if (!string.IsNullOrWhiteSpace(rateText) && !rateText.Contains("%"))
                    rateText = $"{rateText}%";
                if (string.IsNullOrWhiteSpace(rateText))
                    rateText = "-";

                if (price <= 0 && volume <= 0 && string.IsNullOrWhiteSpace(itemName))
                    continue;

                long change = dayChange;
                if (change == 0 && prev > 0 && price > 0)
                    change = price - prev;

                bool isNxtRequest = requestCode.EndsWith("_NX", StringComparison.OrdinalIgnoreCase);

                var parsed = new WatchStockItem
                {
                    Code = code,
                    Name = string.IsNullOrWhiteSpace(itemName) ? (string.IsNullOrWhiteSpace(name) ? code : name) : itemName,
                    CurrentPrice = Math.Abs(price),
                    ChangeAmount = change,
                    ChangeRateText = rateText,
                    VolumeText = volume > 0 ? volume.ToString("N0") : "-",
                    MarketTypeCode = marketInfo?.MarketTypeCode ?? string.Empty,
                    MarketName = marketInfo?.MarketName ?? string.Empty,
                    ProgramMarketType = marketInfo?.ProgramMarketType ?? string.Empty,
                    LastPrice = marketInfo?.LastPrice ?? 0,
                    OrderWarning = marketInfo?.OrderWarning ?? string.Empty,
                    AuditInfo = marketInfo?.AuditInfo ?? string.Empty,
                    StockState = marketInfo?.StockState ?? string.Empty,
                    SectorName = marketInfo?.SectorName ?? string.Empty,
                    SupportsNxt = isNxtRequest
                };

                if (isNxtRequest)
                    nxtItem = parsed;
                else
                    krxItem = parsed;
            }

            if (krxItem != null || nxtItem != null)
            {
                WatchStockItem chosen = krxItem ?? nxtItem!;
                chosen.SupportsNxt = marketInfo?.SupportsNxt ?? nxtItem != null;
                return chosen;
            }

            return fallback;
        }

        private async Task<Dictionary<string, StockMarketInfo>> GetStockMarketInfoMapAsync(string token, CancellationToken cancellationToken)
        {
            var map = new Dictionary<string, StockMarketInfo>(StringComparer.Ordinal);
            foreach ((string RequestMarketType, string ProgramMarketType, string DefaultName) market in new[]
            {
                ("0", "P00101", "KOSPI"),
                ("10", "P10102", "KOSDAQ")
            })
            {
                JsonElement root = await PostApiRootAsync(
                    token,
                    "ka10099",
                    "/api/dostk/stkinfo",
                    new { mrkt_tp = market.RequestMarketType },
                    cancellationToken).ConfigureAwait(false);

                JsonElement rows = FindArrayByKeySafe(root, "list");
                if (rows.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement row in rows.EnumerateArray())
                {
                    string code = NormalizeStockCode(ReadAnyDeep(row, "code", "stk_cd", "stkCd"));
                    if (string.IsNullOrWhiteSpace(code) || map.ContainsKey(code))
                        continue;

                    string marketCode = ReadAnyDeep(row, "marketCode", "market_code", "mrkt_tp");
                    string marketName = ReadAnyDeep(row, "marketName", "market_name");
                    string nxtEnable = ReadAnyDeep(row, "nxtEnable", "nxt_enable");
                    long lastPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "lastPrice", "last_price", "base_pric")));
                    map[code] = new StockMarketInfo
                    {
                        MarketTypeCode = string.IsNullOrWhiteSpace(marketCode) ? market.RequestMarketType : marketCode,
                        MarketName = string.IsNullOrWhiteSpace(marketName) ? market.DefaultName : marketName,
                        ProgramMarketType = market.ProgramMarketType,
                        LastPrice = lastPrice,
                        OrderWarning = ReadAnyDeep(row, "orderWarning", "order_warning"),
                        AuditInfo = ReadAnyDeep(row, "auditInfo", "audit_info"),
                        StockState = ReadAnyDeep(row, "state"),
                        SectorName = ReadAnyDeep(row, "upName", "up_name"),
                        SupportsNxt = string.Equals(nxtEnable, "Y", StringComparison.OrdinalIgnoreCase)
                    };
                }
            }

            return map;
        }

        private sealed class StockMarketInfo
        {
            public string MarketTypeCode { get; set; } = string.Empty;
            public string MarketName { get; set; } = string.Empty;
            public string ProgramMarketType { get; set; } = string.Empty;
            public long LastPrice { get; set; }
            public string OrderWarning { get; set; } = string.Empty;
            public string AuditInfo { get; set; } = string.Empty;
            public string StockState { get; set; } = string.Empty;
            public string SectorName { get; set; } = string.Empty;
            public bool SupportsNxt { get; set; }
        }

        private static string ResolveConditionSeq(JsonElement root, string targetSeq)
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

        private static List<string> ParseConditionStockDisplayNames(JsonElement root)
        {
            var names = new List<string>();
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return names;

            foreach (JsonElement item in data.EnumerateArray())
            {
                string name = ReadConditionName(item);
                string code = ReadConditionCode(item);
                string display = !string.IsNullOrWhiteSpace(name) ? name.Trim() : code;
                if (!string.IsNullOrWhiteSpace(display))
                    names.Add(display);
            }

            return names.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string ReadConditionName(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                string[] keys = { "stk_nm", "stkNm", "name", "jm_name", "jmname" };
                foreach (string key in keys)
                {
                    string value = ReadString(item, key);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                if (item.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Object)
                {
                    foreach (string key in keys)
                    {
                        string value = ReadString(values, key);
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }

            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 1)
            {
                return item[1].ToString();
            }

            return string.Empty;
        }

        private static string ReadConditionCode(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                string[] keys = { "stk_cd", "stkCd", "code", "jm_code", "jmcode", "종목코드" };
                foreach (string key in keys)
                {
                    string value = ReadString(item, key);
                    if (!string.IsNullOrWhiteSpace(value))
                        return NormalizeStockCode(value);
                }

                if (item.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Object)
                {
                    foreach (string key in keys)
                    {
                        string value = ReadString(values, key);
                        if (!string.IsNullOrWhiteSpace(value))
                            return NormalizeStockCode(value);
                    }
                }
            }

            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                return NormalizeStockCode(item[0].ToString());

            return string.Empty;
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return string.Empty;

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.ToString();
                }
            }

            return string.Empty;
        }

        private static string NormalizeDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return string.Empty;

            return int.TryParse(digits, out int number) ? number.ToString() : digits.TrimStart('0');
        }

        private static string NormalizeStockCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return string.Empty;

            return digits.Length >= 6 ? digits.Substring(digits.Length - 6) : digits.PadLeft(6, '0');
        }

        private static List<(string Code, string Name)> ParseConditionBaseItems(JsonElement root)
        {
            var items = new List<(string Code, string Name)>();
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return items;

            foreach (JsonElement item in data.EnumerateArray())
            {
                string code = ReadConditionCode(item);
                string name = ReadConditionName(item);
                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
                    continue;

                items.Add((code, string.IsNullOrWhiteSpace(name) ? code : name.Trim()));
            }

            return items
                .GroupBy(x => x.Code, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
        }

        private static string ReadAny(JsonElement element, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = ReadString(element, key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static string ReadAnyDeep(JsonElement root, params string[] keys)
        {
            string value = ReadAny(root, keys);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out JsonElement data))
                {
                    value = ReadAny(data, keys);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;

                    if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                    {
                        value = ReadAny(data[0], keys);
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }

                if (root.TryGetProperty("output", out JsonElement output))
                {
                    value = ReadAny(output, keys);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return string.Empty;
        }

        private static long ParseLongSafe(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            string clean = value.Replace(",", "").Replace("+", "").Trim();
            while (clean.StartsWith("--", StringComparison.Ordinal))
                clean = "-" + clean.Substring(2);
            return long.TryParse(clean, out long parsed) ? parsed : 0;
        }

        private static long ResolvePreviousClosePrice(JsonElement root)
        {
            return Math.Abs(ParseLongSafe(ReadAnyDeep(
                root,
                "pred_close_pric",
                "predClosePric",
                "pred_close_price",
                "prev_close",
                "prev_close_pric",
                "base_prc",
                "basePrc",
                "std_prc",
                "yday_prc",
                "기준가")));
        }

        private static JsonElement FindFirstArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in root.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Array)
                        return p.Value;

                    if (p.Value.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement child = FindFirstArray(p.Value);
                        if (child.ValueKind == JsonValueKind.Array)
                            return child;
                    }
                }
            }

            return default;
        }

        private static DailyCandle? ParseDailyCandle(JsonElement item)
        {
            string date = ReadAnyDeep(item, "cntr_tm", "time", "tm", "dt", "date", "d", "stk_dt", "?쇱옄");
            if (string.IsNullOrWhiteSpace(date) && item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                date = item[0].ToString();

            date = new string((date ?? string.Empty).Where(char.IsDigit).ToArray());
            if (date.Length >= 14)
                date = date.Substring(0, 14);
            else if (date.Length >= 8)
                date = date.Substring(0, 8);

            long open = Math.Abs(ParseLongSafe(ReadAnyDeep(item, "open_pric", "open", "stck_oprc")));
            long high = Math.Abs(ParseLongSafe(ReadAnyDeep(item, "high_pric", "high", "stck_hgpr")));
            long low = Math.Abs(ParseLongSafe(ReadAnyDeep(item, "low_pric", "low", "stck_lwpr")));
            long close = Math.Abs(ParseLongSafe(ReadAnyDeep(item, "clos_pric", "close", "stck_clpr", "cur_prc")));
            long volume = Math.Abs(ParseLongSafe(ReadAnyDeep(item, "trde_qty", "cntg_vol", "volume", "acml_vol", "acc_trde_qty")));

            if (close == 0 && item.ValueKind == JsonValueKind.Array)
            {
                if (item.GetArrayLength() > 4)
                {
                    open = open == 0 ? Math.Abs(ParseLongSafe(item[1].ToString())) : open;
                    high = high == 0 ? Math.Abs(ParseLongSafe(item[2].ToString())) : high;
                    low = low == 0 ? Math.Abs(ParseLongSafe(item[3].ToString())) : low;
                    close = close == 0 ? Math.Abs(ParseLongSafe(item[4].ToString())) : close;
                }

                if (item.GetArrayLength() > 5)
                    volume = volume == 0 ? Math.Abs(ParseLongSafe(item[5].ToString())) : volume;
            }

            if (close <= 0 || string.IsNullOrWhiteSpace(date))
                return null;

            return new DailyCandle
            {
                Date = date,
                Open = open > 0 ? open : close,
                High = high > 0 ? high : close,
                Low = low > 0 ? low : close,
                Close = close,
                Volume = volume
            };
        }

        private static string FormatKoreanMoney(long value)
        {
            if (value <= 0) return "-";
            if (value >= 1_0000_0000_0000) return $"{value / 1_0000_0000_0000.0:0.0}\uC870";
            if (value >= 100_000_000) return $"{value / 100_000_000.0:0.0}\uC5B5";
            if (value >= 10_000) return $"{value / 10_000.0:0.0}\uB9CC";
            return value.ToString("N0");
        }

        private static string FormatHundredMillionWonUnit(long value)
        {
            if (value <= 0) return "-";
            return $"{value:N0}\uC5B5";
        }

        private static string FormatMillionWonUnit(long value)
        {
            if (value <= 0) return "-";

            decimal hundredMillion = value / 100m;
            if (hundredMillion >= 10m)
                return $"{hundredMillion:N1}\uC5B5";

            return $"{value:N0}\uBC31\uB9CC";
        }
    }
}

