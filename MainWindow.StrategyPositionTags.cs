using System;
using System.Collections.Generic;
using System.Linq;
using TradingDashboard.Models;
using TradingDashboard.Services.Strategies;
using TradingDashboard.Services.Trading;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void LoadStrategyPositionLedger()
        {
            foreach (StrategyPositionLedgerEntry entry in _strategyPositionLedgerStore.LoadToday())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                    _strategyPositionLedgerByKey[entry.Key] = entry;
            }
        }

        private StrategyPositionLedgerEntry? SaveStrategyBuyPosition(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            KiwoomOrderResult orderResult,
            IReadOnlyList<KiwoomFill> fills)
        {
            IReadOnlyList<KiwoomFill> buyFills = [.. (fills ?? [])
                .Where(fill => fill.FilledQuantity > 0 &&
                    string.Equals(NormalizeStockCode(fill.StockCode), NormalizeStockCode(stock.Code), StringComparison.Ordinal) &&
                    IsBuyFill(fill))];

            long quantity = buyFills.Sum(fill => Math.Max(0, fill.FilledQuantity));
            if (quantity <= 0)
                return null;

            long amount = buyFills.Sum(fill => Math.Max(0, fill.FilledQuantity) * Math.Max(0, fill.FilledPrice));
            long averagePrice = amount > 0 ? amount / quantity : 0;
            KiwoomFill firstFill = buyFills
                .OrderBy(fill => ParseFillTime(fill.OrderTime) == DateTime.MinValue ? DateTime.MaxValue : ParseFillTime(fill.OrderTime))
                .First();
            DateTime fillTime = ParseFillTime(firstFill.OrderTime);

            string market = ShouldUseNxtDataForStock(stock.Code) ? "NXT" : "KRX";
            long entryLow = 0;
            DateTime entryBarTime = DateTime.MinValue;
            if (fillTime != DateTime.MinValue &&
                _strategyMinuteCacheService.TryGetBarAt(stock.Code, market, 5, fillTime, out StrategyMinuteBar entryBar))
            {
                entryLow = entryBar.Low;
                entryBarTime = entryBar.BucketTime;
            }

            string slotTag = FormatStrategySlotNumber(result.SlotId);
            var entry = new StrategyPositionLedgerEntry
            {
                Key = BuildStrategyPositionKey(stock.Code, result.SlotId, orderResult.OrderNo),
                Code = NormalizeStockCode(stock.Code),
                Name = stock.Name,
                SlotId = result.SlotId.ToString(),
                SlotTag = slotTag,
                Source = "AUTO",
                BuyOrderNo = orderResult.OrderNo,
                Quantity = quantity,
                OpenQuantity = quantity,
                AveragePrice = averagePrice,
                Entry5MinuteLow = entryLow,
                FillTime = fillTime == DateTime.MinValue ? firstFill.OrderTime : fillTime.ToString("yyyyMMddHHmmss"),
                Entry5MinuteTime = entryBarTime == DateTime.MinValue ? string.Empty : entryBarTime.ToString("yyyyMMddHHmmss"),
                Status = "OPEN",
                Memo = result.Name
            };

            _strategyPositionLedgerStore.UpsertToday(entry);
            _strategyPositionLedgerByKey[entry.Key] = entry;
            return entry;
        }

        private KiwoomHolding DecorateHoldingPositionTag(KiwoomHolding holding)
        {
            string tag = ResolveHoldingPositionTag(holding.StockCode);
            return holding with { PositionTag = tag };
        }

        private string ResolveHoldingPositionTag(string code)
        {
            IReadOnlyList<string> autoTags = ResolveAutomaticStrategyTagsForCode(code);
            if (autoTags.Count > 0)
                return $"AUTO {string.Join(",", autoTags)}";

            return IsManualBuyStopAssistEnabled()
                ? "MANUAL STOP"
                : "MANUAL";
        }

        private bool HasAutomaticStrategyPositionToday(string code) =>
            ResolveAutomaticStrategyTagsForCode(code).Count > 0;

        private IReadOnlyList<string> ResolveAutomaticStrategyTagsForCode(string code)
        {
            string normalizedCode = NormalizeStockCode(code);
            List<string> slots = [.. _strategyPositionLedgerByKey.Values
                .Where(entry => string.Equals(entry.Code, normalizedCode, StringComparison.Ordinal) &&
                    string.Equals(entry.Source, "AUTO", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) &&
                    entry.OpenQuantity > 0)
                .Select(entry => string.IsNullOrWhiteSpace(entry.SlotTag) ? FormatStrategyPositionTag(entry.SlotId) : entry.SlotTag)];

            string prefix = $"{normalizedCode}|";
            string suffix = $"|LIVE_BUY|{DateTime.Today:yyyyMMdd}";

            lock (_strategyLiveOrderLock)
            {
                foreach (string key in _strategyLiveBuyOrderKeys)
                {
                    if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
                        !key.EndsWith(suffix, StringComparison.Ordinal))
                        continue;

                    string[] parts = key.Split('|');
                    if (parts.Length < 2)
                        continue;

                    slots.Add(FormatStrategyPositionTag(parts[1]));
                }
            }

            return [.. slots.Distinct(StringComparer.Ordinal)];
        }

        private string BuildStrategyPositionKey(string code, StrategySlotId slotId, string orderNo)
        {
            string safeOrderNo = string.IsNullOrWhiteSpace(orderNo) ? "NOORDER" : orderNo.Trim();
            return $"{NormalizeStockCode(code)}|{slotId}|{safeOrderNo}|{DateTime.Today:yyyyMMdd}";
        }

        private string FormatStrategyPositionTag(string slotId)
        {
            if (Enum.TryParse(slotId, out StrategySlotId id))
            {
                StrategySlotDescriptor? descriptor = _strategySlotRegistry.GetDescriptor(id);
                if (descriptor != null)
                    return FormatStrategySlotNumber(id);
            }

            return string.IsNullOrWhiteSpace(slotId) ? "STRATEGY" : slotId;
        }

        private static string FormatStrategySlotNumber(StrategySlotId id) =>
            id switch
            {
                StrategySlotId.BaseCandleChase => "Slot 01",
                StrategySlotId.ThreeMinutePullback => "Slot 02",
                StrategySlotId.SorTenMinuteFiveMinuteBreakout => "Slot 03",
                StrategySlotId.ThemeDisclosureAssist => "Slot 04",
                _ => "Slot ??"
            };

        private readonly record struct ManualBuyStopAnchor(
            string Code,
            DateTime EntryBarTime,
            long EntryLow,
            DateTime FillTime,
            DateTime CapturedAt);
    }
}
