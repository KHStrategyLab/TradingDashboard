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
            if (!TryResolveStrategyEntry5MinuteLow(stock, out long entry5MinuteLow, out DateTime entry5MinuteTime))
                return null;

            string exitStrategyCode = ResolveStrategySlotExitStrategyCode(result.SlotId);
            var entry = new PaperPositionLedgerEntry
            {
                Key = key,
                Date = DateTime.Today.ToString("yyyyMMdd"),
                Code = NormalizeStockCode(stock.Code),
                Name = stock.Name,
                SlotTag = FormatStrategySlotNumber(result.SlotId),
                EntryStrategyCode = result.SlotId.ToString(),
                ExitStrategyCode = exitStrategyCode,
                Status = "OPEN",
                Quantity = quantity,
                EntryPrice = price,
                CurrentPrice = price,
                Entry5MinuteLow = entry5MinuteLow,
                ProfitLoss = 0,
                ProfitRate = 0,
                EntryTime = now,
                Entry5MinuteTime = entry5MinuteTime == DateTime.MinValue ? string.Empty : entry5MinuteTime.ToString("yyyyMMddHHmmss"),
                Reason = result.Name,
                UpdatedAt = now
            };

            _paperPositions.Add(entry);
            SavePaperPositions();
            SavePaperTradeMark(entry, "BUY", now, result.Name);
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

            StrategyExitCheck decision = EvaluatePaperExitCheck(entry);
            if (!decision.HasExitSignal)
                return;

            entry.Status = "CLOSED";
            entry.ExitTime = now;
            entry.Reason = string.IsNullOrWhiteSpace(entry.Reason)
                ? decision.Reason
                : $"{entry.Reason} / {decision.Reason}";
            SavePaperTradeMark(entry, decision.Reason, now, decision.Reason);
            AppendReadyLog(
                $"PAPER {decision.Reason} MARKED: {entry.Code} {entry.Name} / {entry.SlotTag} / " +
                $"entry {entry.EntryPrice:N0} / exit {entry.CurrentPrice:N0} / pnl {entry.ProfitLoss:N0} ({entry.ProfitRate:0.##}%)");
        }

        private void SavePaperTradeMark(PaperPositionLedgerEntry entry, string eventName, string time, string reason)
        {
            long price = string.Equals(eventName, "BUY", StringComparison.OrdinalIgnoreCase)
                ? entry.EntryPrice
                : entry.CurrentPrice;

            _paperTradeMarkStore.AppendToday(new PaperTradeMarkEntry
            {
                Id = $"{time}|{entry.Key}|{eventName}",
                Date = DateTime.Today.ToString("yyyyMMdd"),
                Time = time,
                PositionKey = entry.Key,
                Code = entry.Code,
                Name = entry.Name,
                SlotTag = entry.SlotTag,
                EntryStrategyCode = entry.EntryStrategyCode,
                ExitStrategyCode = entry.ExitStrategyCode,
                Event = eventName,
                Quantity = entry.Quantity,
                Price = price,
                Amount = price * entry.Quantity,
                EntryPrice = entry.EntryPrice,
                Entry5MinuteLow = entry.Entry5MinuteLow,
                ProfitLoss = string.Equals(eventName, "BUY", StringComparison.OrdinalIgnoreCase) ? 0 : entry.ProfitLoss,
                ProfitRate = string.Equals(eventName, "BUY", StringComparison.OrdinalIgnoreCase) ? 0 : entry.ProfitRate,
                Reason = reason,
                Memo = "paper only"
            });
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

        private static StrategyExitCheck EvaluatePaperExitCheck(PaperPositionLedgerEntry entry)
        {
            if (entry.CurrentPrice > 0 && entry.EntryPrice > 0 && entry.Entry5MinuteLow > 0 && entry.CurrentPrice <= entry.Entry5MinuteLow)
            {
                return StrategyExitCheck.Signal(
                    "ENTRY_5M_LOW_STOP",
                    entry.CurrentPrice,
                    entry.EntryPrice,
                    CalculateProfitRate(entry.CurrentPrice, entry.EntryPrice),
                    entry.Key,
                    entry.SlotTag,
                    entry.Quantity,
                    entry.ExitStrategyCode);
            }

            return EvaluateExitDecision(
                entry.CurrentPrice,
                entry.EntryPrice,
                entry.Quantity,
                entry.Key,
                entry.SlotTag,
                entry.ExitStrategyCode,
                ParseLedgerTime(entry.EntryTime));
        }
    }
}
