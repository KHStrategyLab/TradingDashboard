using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                throw new InvalidOperationException($"조건식 {(_settings.ConditionSeq01 ?? "1")}을(를) 찾지 못했습니다.");
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
            var result = new List<WatchStockItem>();

            foreach ((string code, string name) in baseItems)
            {
                WatchStockItem item = await GetStockInfoAsync(token, code, name, cancellationToken).ConfigureAwait(false);
                result.Add(item);
            }

            return result;
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

            using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
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

            using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
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

                using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
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
                string floatRatio = ReadAnyDeep(root, "distb_rt", "float_rt", "유통비율", "float_ratio");
                string turnoverRate = ReadAnyDeep(root, "turnover_rt", "trde_rt", "turnoverRate", "회전율");
                string volumeRatio = ReadAnyDeep(root, "vol_rt", "volume_rt", "거래량비율", "volRatio");
                long listedShares = ParseLongSafe(ReadAnyDeep(root, "lst_stk_cnt", "lstStkCnt", "list_stkcnt", "listed_shares", "상장주식수"));

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
                if (listedShares > 0 && listedShares < 100_000_000)
                    listedShares *= 1000;
                long marketCap = (price > 0 && listedShares > 0) ? price * listedShares : 0;

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
            if (basePrice <= 0 && price > 0 && dayDiff != 0)
                basePrice = price - dayDiff;

            string changeRate = ReadAnyDeep(root10007, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (string.IsNullOrWhiteSpace(changeRate))
                changeRate = ReadAnyDeep(root10100, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (string.IsNullOrWhiteSpace(changeRate))
                changeRate = ReadAnyDeep(atn, "flu_rt", "chg_rt", "change_rate", "12");

            long volume = ParseLongSafe(ReadAnyDeep(root10007, "trde_qty", "trdeQty", "acc_trde_qty", "acml_vol", "volume", "13"));
            if (volume <= 0)
                volume = ParseLongSafe(ReadAnyDeep(atn, "trde_qty", "trdeQty", "acc_trde_qty", "acml_vol", "volume", "13"));
            long tradingValue = ParseLongSafe(ReadAnyDeep(root10007, "trde_prica", "trde_amt", "acc_trde_prica", "acc_trde_amt", "acml_tr_pbmn", "14"));
            if (tradingValue <= 0)
                tradingValue = ParseLongSafe(ReadAnyDeep(atn, "trde_prica", "trde_amt", "acc_trde_prica", "acc_trde_amt", "acml_tr_pbmn", "14"));
            bool tradingValueFromApi = tradingValue > 0;
            long marketCap = ParseLongSafe(ReadAnyDeep(atn, "mac", "market_cap"));
            bool marketCapFromApi = marketCap > 0;
            long listedShares = ParseLongSafe(ReadAnyDeep(root10100, "listCount", "stkcnt", "lst_stk_cnt", "list_stkcnt", "listed_shares"));
            if (listedShares <= 0)
                listedShares = ParseLongSafe(ReadAnyDeep(atn, "stkcnt", "listCount", "lst_stk_cnt", "list_stkcnt", "listed_shares"));

            string turnoverRate = ReadAnyDeep(atn, "turnover_rt", "trde_rt", "turnoverRate");
            string volumeRatio = ReadAnyDeep(atn, "vol_rt", "volume_rt", "volRatio");

            if (!string.IsNullOrWhiteSpace(changeRate) && !changeRate.Contains("%")) changeRate += "%";
            if (!string.IsNullOrWhiteSpace(turnoverRate) && !turnoverRate.Contains("%")) turnoverRate += "%";
            if (!string.IsNullOrWhiteSpace(volumeRatio) && !volumeRatio.Contains("%")) volumeRatio += "%";

            if (tradingValue <= 0 && price > 0 && volume > 0)
                tradingValue = price * volume;
            if (marketCap <= 0 && price > 0 && listedShares > 0)
                marketCap = price * listedShares;

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
                ListedSharesText = listedShares > 0 ? listedShares.ToString("N0") : "-",
                TurnoverRateText = string.IsNullOrWhiteSpace(turnoverRate) ? "-" : turnoverRate,
                ChangeRateText = string.IsNullOrWhiteSpace(changeRate) ? "-" : changeRate,
                PrevDiffText = dayDiff == 0 ? "0" : (dayDiff > 0 ? $"+{dayDiff:N0}" : $"{dayDiff:N0}"),
                VolumeRatioText = string.IsNullOrWhiteSpace(volumeRatio) ? "-" : volumeRatio
            };
        }

        public async Task<StockStatusMetrics> GetTodayExecutionSummaryAsync(string code, bool useNxtMarket = false, CancellationToken cancellationToken = default)
        {
            ValidateSettings();
            string token = await IssueTokenAsync(cancellationToken).ConfigureAwait(false);
            string baseCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(baseCode))
                return new StockStatusMetrics();

            string requestCode = useNxtMarket ? $"{baseCode}_NX" : baseCode;
            JsonElement root10055 = await PostApiRootAsync(token, "ka10055", "/api/dostk/stkinfo", new { stk_cd = requestCode, tdy_pred = "1" }, cancellationToken).ConfigureAwait(false);
            JsonElement root90004 = await PostApiRootAsync(token, "ka90004", "/api/dostk/mrkcond", new { stk_cd = requestCode }, cancellationToken).ConfigureAwait(false);

            JsonElement execArray = FindArrayByKeySafe(root10055, "tdy_pred_cntr_qty");
            long buyCum = 0;
            long sellCum = 0;

            if (execArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in execArray.EnumerateArray())
                {
                    buyCum += Math.Max(0, ParseLongSafe(ReadAnyDeep(row, "msqty", "buy_exec_qty", "buy_qty", "1031")));
                    sellCum += Math.Max(0, ParseLongSafe(ReadAnyDeep(row, "mdqty", "sell_exec_qty", "sell_qty", "1030")));
                }
            }

            if (buyCum == 0 && sellCum == 0)
            {
                buyCum = Math.Max(0, ParseLongSafe(ReadAnyDeep(root10055, "msqty", "buy_exec_qty", "buy_qty", "1031")));
                sellCum = Math.Max(0, ParseLongSafe(ReadAnyDeep(root10055, "mdqty", "sell_exec_qty", "sell_qty", "1030")));
            }

            long programBuy = Math.Max(0, ParseLongSafe(ReadAnyDeep(root90004, "prog_buy_qty", "prgm_buy_qty", "buy_qty", "net_buy_qty")));

            return new StockStatusMetrics
            {
                BuyExecCum = buyCum,
                SellExecCum = sellCum,
                ProgramBuyText = programBuy > 0 ? programBuy.ToString("N0") : "-"
            };
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
            JsonElement execRoot = await PostApiRootAsync(token, "ka10055", "/api/dostk/stkinfo", new { stk_cd = requestCode, tdy_pred = "1" }, cancellationToken).ConfigureAwait(false);

            snapshot.CurrentPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(quoteRoot, "cur_prc", "curPrc", "now_prc", "price", "10")));
            snapshot.DayChange = ParseLongSafe(ReadAnyDeep(quoteRoot, "pred_pre", "predPre", "change", "chg_val", "11"));
            string rate = ReadAnyDeep(quoteRoot, "flu_rt", "fluRt", "chg_rt", "change_rate", "12");
            if (!string.IsNullOrWhiteSpace(rate) && !rate.Contains("%"))
                rate += "%";
            snapshot.ChangeRateText = string.IsNullOrWhiteSpace(rate) ? "-" : rate;

            long basePrice = Math.Abs(ParseLongSafe(ReadAnyDeep(quoteRoot, "base_prc", "basePrc", "std_prc", "yday_prc", "기준가")));
            if (basePrice <= 0 && snapshot.CurrentPrice > 0 && snapshot.DayChange != 0)
                basePrice = snapshot.CurrentPrice - snapshot.DayChange;
            snapshot.BasePrice = Math.Max(0, basePrice);

            for (int i = 0; i < 10; i++)
            {
                int level = i + 1;
                long sellPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (41 + i).ToString(), $"sel_{level}bid", $"sel_{level}th_pre_bid", $"sell_{level}_price", $"ask_{level}_price")));
                long sellQty = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (61 + i).ToString(), $"sel_{level}bid_req", $"sel_{level}th_pre_req", $"sell_{level}_qty", $"ask_{level}_qty")));
                if (sellPrice > 0 || sellQty > 0)
                    snapshot.SellLevels.Add(new HogaQuoteLevel { Price = sellPrice, Quantity = sellQty });

                long buyPrice = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (51 + i).ToString(), $"buy_{level}bid", $"buy_{level}th_pre_bid", $"buy_{level}_price", $"bid_{level}_price")));
                long buyQty = Math.Abs(ParseLongSafe(ReadAnyDeep(hogaRoot, (71 + i).ToString(), $"buy_{level}bid_req", $"buy_{level}th_pre_req", $"buy_{level}_qty", $"bid_{level}_qty")));
                if (buyPrice > 0 || buyQty > 0)
                    snapshot.BuyLevels.Add(new HogaQuoteLevel { Price = buyPrice, Quantity = buyQty });
            }

            JsonElement execArray = FindArrayByKeySafe(execRoot, "tdy_pred_cntr_qty");
            if (execArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in execArray.EnumerateArray())
                {
                    long signedQty = ParseLongSafe(ReadAnyDeep(row, "cntr_qty", "trade_qty", "15"));
                    if (signedQty > 0)
                        snapshot.BuyExecCum += signedQty;
                    else if (signedQty < 0)
                        snapshot.SellExecCum += Math.Abs(signedQty);

                    if (snapshot.RecentTrades.Count >= 10)
                        continue;

                    long tradePrice = Math.Abs(ParseLongSafe(ReadAnyDeep(row, "cntr_pric", "cntr_price", "trade_price", "10")));
                    long qty = Math.Abs(signedQty);
                    if (tradePrice > 0 || qty > 0)
                    {
                        snapshot.RecentTrades.Add(new ClosingTradePrint
                        {
                            Price = tradePrice,
                            Quantity = qty,
                            IsBuyAggressive = signedQty >= 0
                        });
                    }
                }
            }

            if (snapshot.BuyExecCum == 0 && snapshot.SellExecCum == 0)
            {
                snapshot.BuyExecCum = Math.Max(0, ParseLongSafe(ReadAnyDeep(execRoot, "msqty", "buy_exec_qty", "buy_qty", "1031")));
                snapshot.SellExecCum = Math.Max(0, ParseLongSafe(ReadAnyDeep(execRoot, "mdqty", "sell_exec_qty", "sell_qty", "1030")));
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

            using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
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

            using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                return JsonDocument.Parse("{}").RootElement;

            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

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
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/oauth2/token");
            string payload = JsonSerializer.Serialize(new
            {
                grant_type = "client_credentials",
                appkey = _settings.AppKey,
                secretkey = _settings.SecretKey
            });
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(json);
            string token = ReadString(doc.RootElement, "token");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("키움 토큰 응답에 token 값이 없습니다.");

            return token;
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
                throw new InvalidOperationException($"조건식 {(_settings.ConditionSeq01 ?? "1")}을(를) 찾지 못했습니다.");

            await SendJsonAsync(ws, new { trnm = "CNSRREQ", seq, search_type = "1", stex_tp = "K", cont_yn = "N", next_key = "" }, cancellationToken).ConfigureAwait(false);
            using JsonDocument condRes = await ReceiveByTrNameAsync(ws, "CNSRREQ", TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            return ParseConditionBaseItems(condRes.RootElement);
        }

        private async Task<WatchStockItem> GetStockInfoAsync(string token, string code, string name, CancellationToken cancellationToken)
        {
            var fallback = new WatchStockItem
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(name) ? code : name,
                VolumeText = "-",
                ChangeRateText = "-",
                SupportsNxt = false
            };

            if (string.IsNullOrWhiteSpace(code))
                return fallback;

            WatchStockItem? krxItem = null;
            WatchStockItem? sorItem = null;

            foreach (string requestCode in new[] { code, $"{code}_AL" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/stkinfo");
                req.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
                req.Headers.TryAddWithoutValidation("api-id", "ka10001");
                req.Headers.TryAddWithoutValidation("cont-yn", "N");
                req.Headers.TryAddWithoutValidation("next-key", "");
                req.Content = new StringContent(JsonSerializer.Serialize(new { stk_cd = requestCode }), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
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

                var parsed = new WatchStockItem
                {
                    Code = code,
                    Name = string.IsNullOrWhiteSpace(itemName) ? (string.IsNullOrWhiteSpace(name) ? code : name) : itemName,
                    CurrentPrice = Math.Abs(price),
                    ChangeAmount = change,
                    ChangeRateText = rateText,
                    VolumeText = volume > 0 ? volume.ToString("N0") : "-",
                    SupportsNxt = requestCode.EndsWith("_AL", StringComparison.OrdinalIgnoreCase)
                };

                if (requestCode.EndsWith("_AL", StringComparison.OrdinalIgnoreCase))
                    sorItem = parsed;
                else
                    krxItem = parsed;
            }

            if (krxItem != null || sorItem != null)
            {
                WatchStockItem chosen = krxItem ?? sorItem!;
                chosen.SupportsNxt = sorItem != null;
                return chosen;
            }

            return fallback;
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
                string[] keys = { "stk_nm", "stkNm", "name", "jm_name", "jmname", "종목명" };
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
            return long.TryParse(clean, out long parsed) ? parsed : 0;
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
            string date = ReadAnyDeep(item, "dt", "date", "d", "stk_dt", "일자");
            if (string.IsNullOrWhiteSpace(date) && item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                date = item[0].ToString();

            date = new string((date ?? string.Empty).Where(char.IsDigit).ToArray());
            if (date.Length >= 8) date = date.Substring(0, 8);

            long open = ParseLongSafe(ReadAnyDeep(item, "open_pric", "open", "stck_oprc", "시가"));
            long high = ParseLongSafe(ReadAnyDeep(item, "high_pric", "high", "stck_hgpr", "고가"));
            long low = ParseLongSafe(ReadAnyDeep(item, "low_pric", "low", "stck_lwpr", "저가"));
            long close = ParseLongSafe(ReadAnyDeep(item, "clos_pric", "close", "stck_clpr", "cur_prc", "종가", "현재가"));
            long volume = ParseLongSafe(ReadAnyDeep(item, "acml_vol", "acc_trde_qty", "trde_qty", "volume", "거래량"));

            if (close == 0 && item.ValueKind == JsonValueKind.Array)
            {
                if (item.GetArrayLength() > 4)
                {
                    open = open == 0 ? ParseLongSafe(item[1].ToString()) : open;
                    high = high == 0 ? ParseLongSafe(item[2].ToString()) : high;
                    low = low == 0 ? ParseLongSafe(item[3].ToString()) : low;
                    close = close == 0 ? ParseLongSafe(item[4].ToString()) : close;
                }

                if (item.GetArrayLength() > 5)
                    volume = volume == 0 ? ParseLongSafe(item[5].ToString()) : volume;
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
            if (value >= 1_0000_0000_0000) return $"{value / 1_0000_0000_0000.0:0.0}조";
            if (value >= 100_000_000) return $"{value / 100_000_000.0:0.0}억";
            if (value >= 10_000) return $"{value / 10_000.0:0.0}만";
            return value.ToString("N0");
        }

        private static string FormatHundredMillionWonUnit(long value)
        {
            if (value <= 0) return "-";
            return $"{value:N0}억";
        }

        private static string FormatMillionWonUnit(long value)
        {
            if (value <= 0) return "-";

            decimal hundredMillion = value / 100m;
            if (hundredMillion >= 10m)
                return $"{hundredMillion:N1}억";

            return $"{value:N0}백만";
        }
    }
}
