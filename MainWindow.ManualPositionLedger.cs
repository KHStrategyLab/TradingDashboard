using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TradingDashboard.Models;
using TradingDashboard.Services.Trading;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void LoadManualPositionLedger()
        {
            foreach (ManualPositionLedgerEntry entry in _manualPositionLedgerStore.LoadToday())
            {
                if (!string.IsNullOrWhiteSpace(entry.Code))
                    _manualPositionLedgerByCode[NormalizeStockCode(entry.Code)] = entry;
            }
        }

        private void SyncManualPositionLedger(IReadOnlyList<KiwoomHolding> holdings)
        {
            HashSet<string> holdingCodes = [];

            foreach (KiwoomHolding holding in holdings ?? [])
            {
                if (holding.HoldingQuantity <= 0)
                    continue;

                string code = NormalizeStockCode(holding.StockCode);
                holdingCodes.Add(code);
                if (HasAutomaticStrategyPositionToday(code))
                    continue;

                ManualPositionLedgerEntry entry = EnsureManualPositionEntry(holding);
                if (!string.Equals(entry.Status, "EXCLUDED", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBalanceToManualPosition(entry, holding);
                    entry.Status = "OPEN";
                    entry.Memo = string.IsNullOrWhiteSpace(entry.Memo) ? "balance sync" : entry.Memo;
                    SaveManualPositionEntry(entry);
                }
            }

            foreach (ManualPositionLedgerEntry entry in _manualPositionLedgerByCode.Values
                .Where(x => string.Equals(x.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                    !holdingCodes.Contains(NormalizeStockCode(x.Code)))
                .ToList())
            {
                entry.OpenQuantity = 0;
                entry.Status = "CLOSED";
                entry.Memo = AppendLedgerMemo(entry.Memo, "balance missing");
                SaveManualPositionEntry(entry);
            }
        }

        private ManualPositionLedgerEntry EnsureManualPositionEntry(KiwoomHolding holding)
        {
            string code = NormalizeStockCode(holding.StockCode);
            if (_manualPositionLedgerByCode.TryGetValue(code, out ManualPositionLedgerEntry? existing))
                return existing;

            var entry = new ManualPositionLedgerEntry
            {
                Key = BuildManualPositionKey(code),
                Code = code,
                Name = holding.StockName,
                Quantity = holding.HoldingQuantity,
                OpenQuantity = holding.OrderableQuantity > 0 ? holding.OrderableQuantity : holding.HoldingQuantity,
                AveragePrice = holding.AverageBuyPrice,
                Status = "OPEN",
                Memo = "balance sync"
            };
            SaveManualPositionEntry(entry);
            return entry;
        }

        private void SaveManualPositionEntry(ManualPositionLedgerEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Code))
                return;

            entry.Key = string.IsNullOrWhiteSpace(entry.Key) ? BuildManualPositionKey(entry.Code) : entry.Key;
            _manualPositionLedgerStore.UpsertToday(entry);
            _manualPositionLedgerByCode[NormalizeStockCode(entry.Code)] = entry;
        }

        private bool TryResolveManualPosition(string code, out ManualPositionLedgerEntry entry)
        {
            return _manualPositionLedgerByCode.TryGetValue(NormalizeStockCode(code), out entry!);
        }

        private string ResolveManualPositionTag(string code)
        {
            if (!TryResolveManualPosition(code, out ManualPositionLedgerEntry entry))
                return IsManualBuyStopAssistEnabled() ? "MANUAL WAIT" : "MANUAL";

            return entry.Status.ToUpperInvariant() switch
            {
                "EXCLUDED" => "MANUAL OFF",
                "CLOSED" => "MANUAL CLOSED",
                _ => entry.Entry5MinuteLow > 0 ? "MANUAL STOP" : "MANUAL WAIT"
            };
        }

        private bool IsManualPositionAssistOpen(string code) =>
            TryResolveManualPosition(code, out ManualPositionLedgerEntry entry) &&
            string.Equals(entry.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
            entry.OpenQuantity > 0;

        private void ApplyManualPositionAnchor(WatchStockItem stock, ManualBuyStopAnchor anchor)
        {
            string code = NormalizeStockCode(stock.Code);
            if (!_manualPositionLedgerByCode.TryGetValue(code, out ManualPositionLedgerEntry? entry))
            {
                entry = new ManualPositionLedgerEntry
                {
                    Key = BuildManualPositionKey(code),
                    Code = code,
                    Name = stock.Name,
                    Status = "OPEN",
                    Memo = "anchor sync"
                };
            }

            entry.Entry5MinuteLow = anchor.EntryLow;
            entry.Entry5MinuteTime = anchor.EntryBarTime == DateTime.MinValue ? entry.Entry5MinuteTime : anchor.EntryBarTime.ToString("yyyyMMddHHmmss");
            entry.FillTime = anchor.FillTime == DateTime.MinValue ? entry.FillTime : anchor.FillTime.ToString("yyyyMMddHHmmss");
            SaveManualPositionEntry(entry);
        }

        private void ApplyManualPositionSellFill(StrategyExitCheck check, long filledQuantity)
        {
            if (filledQuantity <= 0 ||
                string.IsNullOrWhiteSpace(check.PositionKey) ||
                !check.PositionKey.StartsWith("MANUAL|", StringComparison.Ordinal))
                return;

            ManualPositionLedgerEntry? entry = _manualPositionLedgerByCode.Values
                .FirstOrDefault(x => string.Equals(x.Key, check.PositionKey, StringComparison.Ordinal));
            if (entry == null)
                return;

            long sold = Math.Min(filledQuantity, Math.Max(0, entry.OpenQuantity));
            entry.OpenQuantity = Math.Max(0, entry.OpenQuantity - sold);
            if (entry.OpenQuantity <= 0)
                entry.Status = "CLOSED";

            string fillMemo = entry.OpenQuantity <= 0
                ? check.Reason
                : $"{check.Reason} PARTIAL {sold:N0}";
            entry.Memo = AppendLedgerMemo(entry.Memo, fillMemo);
            SaveManualPositionEntry(entry);
        }

        private void ManualPositionIncludeButton_Click(object sender, RoutedEventArgs e)
        {
            KiwoomHolding? holding = GetSelectedBalanceHolding();
            if (holding == null)
                return;

            ManualPositionLedgerEntry entry = EnsureManualPositionEntry(holding);
            ApplyBalanceToManualPosition(entry, holding);
            entry.Status = "OPEN";
            entry.Memo = AppendLedgerMemo(entry.Memo, "manual included");
            SaveManualPositionEntry(entry);
            RefreshDecoratedBalanceRows();
            AppendLog($"manual position included: {holding.StockCode} {holding.StockName}");
        }

        private void ManualPositionExcludeButton_Click(object sender, RoutedEventArgs e)
        {
            KiwoomHolding? holding = GetSelectedBalanceHolding();
            if (holding == null)
                return;

            ManualPositionLedgerEntry entry = EnsureManualPositionEntry(holding);
            entry.Status = "EXCLUDED";
            entry.Memo = AppendLedgerMemo(entry.Memo, "manual excluded");
            SaveManualPositionEntry(entry);
            RefreshDecoratedBalanceRows();
            AppendLog($"manual position excluded: {holding.StockCode} {holding.StockName}");
        }

        private KiwoomHolding? GetSelectedBalanceHolding() =>
            BalanceHoldingsScrollableDataGrid?.SelectedItem as KiwoomHolding ??
            BalanceHoldingsDataGrid?.SelectedItem as KiwoomHolding;

        private async void BalanceHoldingsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_balanceGridSelectionSyncing)
                return;

            _balanceGridSelectionSyncing = true;
            try
            {
                if (sender == BalanceHoldingsDataGrid && BalanceHoldingsScrollableDataGrid != null)
                    BalanceHoldingsScrollableDataGrid.SelectedItem = BalanceHoldingsDataGrid.SelectedItem;
                else if (sender == BalanceHoldingsScrollableDataGrid && BalanceHoldingsDataGrid != null)
                    BalanceHoldingsDataGrid.SelectedItem = BalanceHoldingsScrollableDataGrid.SelectedItem;
            }
            finally
            {
                _balanceGridSelectionSyncing = false;
            }

            if (GetSelectedBalanceHolding() is KiwoomHolding holding)
                await OpenBalanceHoldingAsync(holding);
        }

        private async Task OpenBalanceHoldingAsync(KiwoomHolding holding)
        {
            if (holding == null || string.IsNullOrWhiteSpace(holding.StockCode))
                return;

            string code = NormalizeStockCode(holding.StockCode);
            if (string.IsNullOrWhiteSpace(code))
                return;

            WatchStockItem? stock = _watchStocks
                .Concat(_recentViewedStocks)
                .FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

            if (stock == null && !_watchStockByCode.TryGetValue(code, out stock))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    stock = await _kiwoomConditionService.SearchListedStockAsync(code, cts.Token);
                }
                catch (Exception ex)
                {
                    AppendLog($"balance stock open lookup error: {code} / {ex.GetType().Name} / {ex.Message}");
                }
            }

            stock ??= new WatchStockItem
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(holding.StockName) ? code : holding.StockName
            };

            if (string.IsNullOrWhiteSpace(stock.Name) && !string.IsNullOrWhiteSpace(holding.StockName))
                stock.Name = holding.StockName;
            if (stock.CurrentPrice <= 0 && holding.CurrentPrice > 0)
                ApplyWatchStockDisplayPrice(stock, holding.CurrentPrice, ResolveCachedPriceMarket(stock), "balance holding open");

            await EnsureRealtime0BTrackingAsync(stock, "balance");
            AddRecentViewedStock(stock);
            if (ReferenceEquals(RecentWatchListBox.SelectedItem, stock))
                await LoadNewsForSelectedStockAsync(stock);
            else
                RecentWatchListBox.SelectedItem = stock;
            FocusSelectedRecentStock();
        }

        private void RefreshDecoratedBalanceRows()
        {
            List<KiwoomHolding> holdings = [.. _balanceHoldings];
            _balanceHoldings.Clear();
            foreach (KiwoomHolding holding in holdings)
                _balanceHoldings.Add(DecorateHoldingPositionTag(holding));
        }

        private static void ApplyBalanceToManualPosition(ManualPositionLedgerEntry entry, KiwoomHolding holding)
        {
            entry.Name = string.IsNullOrWhiteSpace(holding.StockName) ? entry.Name : holding.StockName;
            entry.Quantity = holding.HoldingQuantity;
            entry.OpenQuantity = holding.OrderableQuantity > 0 ? holding.OrderableQuantity : holding.HoldingQuantity;
            entry.AveragePrice = holding.AverageBuyPrice;
        }

        private static string BuildManualPositionKey(string code) =>
            $"MANUAL|{NormalizeStockCode(code)}|{DateTime.Today:yyyyMMdd}";

        private static string AppendLedgerMemo(string memo, string addition) =>
            string.IsNullOrWhiteSpace(memo) ? addition : $"{memo} / {addition}";
    }
}
