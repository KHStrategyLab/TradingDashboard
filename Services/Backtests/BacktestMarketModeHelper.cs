using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    internal static class BacktestMarketModeHelper
    {
        public static bool IsSorMixed(BacktestSettings? settings)
        {
            string mode = (settings?.MarketMode ?? string.Empty).Trim();
            return mode.Contains("SOR", StringComparison.OrdinalIgnoreCase) ||
                mode.Contains("MIXED", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<IReadOnlyDictionary<string, StockMasterItem>> LoadStockMasterByCodeAsync(CancellationToken cancellationToken = default)
        {
            var store = new StockMasterCacheStore();
            StockMasterCacheDocument? document = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (document?.Items is not { Count: > 0 })
                return new Dictionary<string, StockMasterItem>(StringComparer.Ordinal);

            return document.Items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => BacktestDataStore.NormalizeCode(item.Code), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        public static IReadOnlyList<BacktestBaseCandle> BuildNxtExecutionBaseCandles(
            IEnumerable<BacktestBaseCandle> source,
            IReadOnlyDictionary<string, StockMasterItem> stockMasterByCode,
            string sourceNameSuffix = "SOR_MIXED_KRX_BASE")
        {
            if (stockMasterByCode.Count == 0)
                return [];

            var mirrors = new List<BacktestBaseCandle>();
            foreach (BacktestBaseCandle item in source ?? [])
            {
                string code = BacktestDataStore.NormalizeCode(item.Code);
                if (string.IsNullOrWhiteSpace(code) ||
                    !string.Equals(BacktestDataStore.NormalizeMarket(item.Market), "KRX", StringComparison.Ordinal) ||
                    !stockMasterByCode.TryGetValue(code, out StockMasterItem? master) ||
                    !master.SupportsNxt)
                {
                    continue;
                }

                string date = BacktestDataStore.NormalizeDate(item.BaseCandleDate);
                if (string.IsNullOrWhiteSpace(date))
                    continue;

                mirrors.Add(new BacktestBaseCandle
                {
                    Key = DailyBaseCandleVerifier.BuildBaseCandleKey(code, "NXT", date),
                    Code = code,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? master.Name : item.Name,
                    Market = "NXT",
                    BaseCandleMarket = "KRX",
                    BaseCandleDate = date,
                    BaseOpen = item.BaseOpen,
                    BaseHigh = item.BaseHigh,
                    BaseLow = item.BaseLow,
                    BaseClose = item.BaseClose,
                    BaseVolume = item.BaseVolume,
                    BaseTradingValue = item.BaseTradingValue,
                    PreviousClose = item.PreviousClose,
                    ChangeRate = item.ChangeRate,
                    CloseLocationPercent = item.CloseLocationPercent,
                    UpperTailPercent = item.UpperTailPercent,
                    Status = item.Status,
                    SourceName = string.IsNullOrWhiteSpace(item.SourceName)
                        ? sourceNameSuffix
                        : $"{item.SourceName}|{sourceNameSuffix}",
                    VerifiedAt = DateTime.Now.ToString("yyyyMMddHHmmss")
                });
            }

            return mirrors;
        }

        public static bool PassesMarketFilter(string market, string filter)
        {
            string normalizedFilter = BacktestDataStore.NormalizeMarket(filter);
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return string.Equals(BacktestDataStore.NormalizeMarket(market), normalizedFilter, StringComparison.Ordinal);
        }
    }
}
