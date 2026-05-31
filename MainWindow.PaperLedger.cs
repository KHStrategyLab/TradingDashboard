using System;
using System.Linq;
using TradingDashboard.Models;
using TradingDashboard.Services.Strategies;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void LoadPaperPositionLedger()
        {
            _paperPositions.Clear();
            foreach (PaperPositionLedgerEntry entry in _paperPositionLedgerStore.LoadToday())
                _paperPositions.Add(entry);
        }

        private PaperPositionLedgerEntry? TryRecordPaperBuy(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            StrategyExecutionSettings execution)
        {
            string key = BuildPaperPositionKey(stock.Code, result.SlotId);
            if (_paperPositions.Any(entry =>
                string.Equals(entry.Key, key, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "OPEN", StringComparison.OrdinalIgnoreCase)))
                return null;

            long price = ResolveStrategySignalPrice(stock);
            if (price <= 0)
                return null;

            long slotCount = Math.Max(1, execution.SlotCount);
            long perSlotBudget = Math.Max(0, execution.Budget) / slotCount;
            long quantity = perSlotBudget / price;
            if (quantity <= 0)
                return null;

            string now = DateTime.Now.ToString("yyyyMMddHHmmss");
            var entry = new PaperPositionLedgerEntry
            {
                Key = key,
                Date = DateTime.Today.ToString("yyyyMMdd"),
                Code = NormalizeStockCode(stock.Code),
                Name = stock.Name,
                SlotTag = FormatStrategySlotNumber(result.SlotId),
                Status = "OPEN",
                Quantity = quantity,
                EntryPrice = price,
                CurrentPrice = price,
                ProfitLoss = 0,
                ProfitRate = 0,
                EntryTime = now,
                Reason = result.Name,
                UpdatedAt = now
            };

            _paperPositions.Add(entry);
            SavePaperPositions();
            return entry;
        }

        private void UpdatePaperPositionsForPrice(string code, long currentPrice)
        {
            string normalizedCode = NormalizeStockCode(code);
            if (string.IsNullOrWhiteSpace(normalizedCode) || currentPrice <= 0)
                return;

            bool changed = false;
            string now = DateTime.Now.ToString("yyyyMMddHHmmss");
            foreach (PaperPositionLedgerEntry entry in _paperPositions.Where(x =>
                string.Equals(x.Code, normalizedCode, StringComparison.Ordinal) &&
                string.Equals(x.Status, "OPEN", StringComparison.OrdinalIgnoreCase)))
            {
                if (entry.CurrentPrice == currentPrice)
                    continue;

                entry.CurrentPrice = currentPrice;
                entry.ProfitLoss = CalculatePaperProfitLoss(entry);
                entry.ProfitRate = CalculatePaperProfitRate(entry);
                entry.UpdatedAt = now;
                TryClosePaperPosition(entry, now);
                changed = true;
            }

            if (changed)
                SavePaperPositions();
        }

        private static long CalculatePaperProfitLoss(PaperPositionLedgerEntry entry)
        {
            if (entry.Quantity <= 0 || entry.EntryPrice <= 0 || entry.CurrentPrice <= 0)
                return 0;

            return (entry.CurrentPrice - entry.EntryPrice) * entry.Quantity;
        }

        private static decimal CalculatePaperProfitRate(PaperPositionLedgerEntry entry)
        {
            if (entry.EntryPrice <= 0 || entry.CurrentPrice <= 0)
                return 0;

            return (entry.CurrentPrice - entry.EntryPrice) / (decimal)entry.EntryPrice * 100m;
        }

        private void TryClosePaperPosition(PaperPositionLedgerEntry entry, string now)
        {
            if (!string.Equals(entry.Status, "OPEN", StringComparison.OrdinalIgnoreCase))
                return;

            string exitReason = string.Empty;
            if (entry.ProfitRate <= StrategyStopLossRate)
                exitReason = "STOP";
            else if (entry.ProfitRate >= StrategyFirstTargetRate)
                exitReason = "TARGET1";

            if (string.IsNullOrWhiteSpace(exitReason))
                return;

            entry.Status = "CLOSED";
            entry.ExitTime = now;
            entry.Reason = string.IsNullOrWhiteSpace(entry.Reason)
                ? exitReason
                : $"{entry.Reason} / {exitReason}";
            AppendReadyLog(
                $"PAPER {exitReason} MARKED: {entry.Code} {entry.Name} / {entry.SlotTag} / " +
                $"entry {entry.EntryPrice:N0} / exit {entry.CurrentPrice:N0} / pnl {entry.ProfitLoss:N0} ({entry.ProfitRate:0.##}%)");
        }

        private void SavePaperPositions()
        {
            foreach (PaperPositionLedgerEntry entry in _paperPositions)
                entry.UpdatedAt = string.IsNullOrWhiteSpace(entry.UpdatedAt)
                    ? DateTime.Now.ToString("yyyyMMddHHmmss")
                    : entry.UpdatedAt;

            _paperPositionLedgerStore.SaveToday(_paperPositions);
        }

        private string BuildPaperPositionKey(string code, StrategySlotId slotId) =>
            $"{NormalizeStockCode(code)}|{slotId}|PAPER|{DateTime.Today:yyyyMMdd}";
    }
}
