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
