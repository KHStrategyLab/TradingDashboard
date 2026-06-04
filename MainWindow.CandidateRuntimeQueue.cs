using System;
using TradingDashboard.Models;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void QueueRuntimeCandidate(WatchStockItem stock, string source)
        {
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
                return;

            string code = NormalizeStockCode(stock.Code);
            if (string.IsNullOrWhiteSpace(code))
                return;

            string now = DateTime.Now.ToString("yyyyMMddHHmmss");
            var entry = new CandidateRuntimeEntry
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(stock.Name) ? code : stock.Name,
                Market = ShouldUseNxtDataForStock(stock) ? "NXT" : "KRX",
                CandidateDate = now[..8],
                CandidateTime = now,
                Source = source,
                ConditionName = GetConfiguredConditionLabel(),
                ConditionId = _config.Kiwoom.ConditionSeq01 ?? string.Empty,
                CurrentPrice = stock.CurrentPrice,
                ChangeAmount = stock.ChangeAmount,
                ChangeRateText = string.IsNullOrWhiteSpace(stock.ChangeRateText) ? "-" : stock.ChangeRateText,
                VolumeText = string.IsNullOrWhiteSpace(stock.VolumeText) ? "-" : stock.VolumeText,
                TradingValue = stock.TodayTradeValue,
                NxtEnabled = stock.SupportsNxt,
                MarketTypeCode = stock.MarketTypeCode,
                MarketName = stock.MarketName,
                ProgramMarketType = stock.ProgramMarketType,
                OrderWarning = stock.OrderWarning,
                AuditInfo = stock.AuditInfo,
                StockState = stock.StockState,
                SectorName = stock.SectorName,
                UpdatedAt = now
            };

            bool added = _candidateRuntimeQueue.AddOrUpdate(entry, out CandidateRuntimeEntry stored);
            AppendLog(added
                ? $"candidate runtime queued: {stored.Name} / {stored.Code} / {stored.Market} / {source}"
                : $"candidate runtime updated: {stored.Name} / {stored.Code} / {stored.Market} / {source}");
        }

        private bool TryGetRuntimeCandidate(string code, out CandidateRuntimeEntry? entry) =>
            _candidateRuntimeQueue.TryGet(code, out entry);
    }
}
