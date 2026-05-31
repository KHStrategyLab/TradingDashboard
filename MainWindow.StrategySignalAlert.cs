using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;
using TradingDashboard.Services.Strategies;
using TradingDashboard.Services.Trading;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private void LoadStrategyOrderJournal()
        {
            IReadOnlyList<StrategyOrderJournalEntry> entries = _strategyOrderJournalStore.LoadToday();
            lock (_strategyLiveOrderLock)
            {
                foreach (StrategyOrderJournalEntry entry in entries.Where(x => x.Success && !string.IsNullOrWhiteSpace(x.Key)))
                {
                    if (string.Equals(entry.Side, "BUY", StringComparison.OrdinalIgnoreCase))
                        _strategyLiveBuyOrderKeys.Add(entry.Key);
                    else if (string.Equals(entry.Side, "SELL", StringComparison.OrdinalIgnoreCase))
                        _strategyLiveSellOrderKeys.Add(entry.Key);
                }
            }
        }

        private void ProcessStrategySignalAlerts(
            WatchStockItem stock,
            IReadOnlyList<StrategyEvaluationResult> results)
        {
            StrategyExecutionSettings execution = GetStrategyExecutionSettings();
            if (!execution.AutoTradingEnabled ||
                stock == null ||
                string.IsNullOrWhiteSpace(stock.Code) ||
                !IsStrategyMinuteDataReady(stock))
                return;

            foreach (StrategyEvaluationResult result in results.Where(x => x.HasSignal))
            {
                if (execution.AllowsLiveBuy)
                    _ = TrySubmitStrategyLiveBuyAsync(stock, result, execution);

                string key = BuildStrategySignalAlertKey(stock, result);
                if (!_strategySignalAlertLoggedKeys.Add(key))
                    continue;

                string orderMode = execution.LiveBuyEnabled
                    ? "LIVE ORDERS ON / order handoff pending"
                    : "LIVE ORDERS OFF / alert only";

                AppendReadyLog(
                    $"STRATEGY BUY SIGNAL: {stock.Code} {stock.Name} / {result.Name} / " +
                    $"price {ResolveStrategySignalPrice(stock):N0} / {orderMode}");

                _ = TrySendStrategySignalAlertAsync(stock, result, orderMode);
            }
        }

        private async Task TrySubmitStrategyLiveBuyAsync(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            StrategyExecutionSettings execution,
            CancellationToken cancellationToken = default)
        {
            string key = BuildStrategyLiveBuyOrderKey(stock, result);
            if (IsStrategyLiveOrderInCooldown(key))
                return;

            StrategyLiveBuyGuardResult guard = EvaluateLiveBuyRiskGuard(stock, result, execution);
            if (!guard.Allowed)
            {
                LogStrategyLiveOrderBlockedOnce(key, $"LIVE BUY BLOCKED: {stock.Code} {stock.Name} / {result.Name} / {guard.Reason}");
                return;
            }

            if (!ReserveStrategyLiveBuyOrderKey(key))
                return;

            try
            {
                KiwoomOrderRequest request = KiwoomOrderRequest.SorLimitFromCurrentPrice(
                    stock.Code,
                    guard.Quantity,
                    guard.ReferencePrice,
                    tickOffset: 1);

                KiwoomOrderResult orderResult = await _tradingClient.BuyAsync(request, cancellationToken).ConfigureAwait(false);
                if (!orderResult.Success)
                {
                    ReleaseStrategyLiveBuyOrderKey(key);
                    SetStrategyLiveOrderCooldown(key, TimeSpan.FromSeconds(60));
                }
                SaveStrategyOrderJournal(
                    key,
                    "BUY",
                    stock,
                    result.SlotId.ToString(),
                    result.Name,
                    guard.Quantity,
                    guard.ReferencePrice,
                    request.OrderPrice,
                    orderResult,
                    "SUBMITTED");

                Dispatcher.Invoke(() =>
                {
                    string status = orderResult.Success ? "SENT" : "FAILED";
                    AppendReadyLog(
                        $"LIVE BUY {status}: {stock.Code} {stock.Name} / {result.Name} / " +
                        $"qty {guard.Quantity:N0} / limit {request.OrderPrice:N0} / ref {guard.ReferencePrice:N0} / " +
                        $"order {orderResult.OrderNo} / {orderResult.ReturnCode} {orderResult.ReturnMessage}");
                });

                if (orderResult.Success)
                    await AuditStrategyLiveBuyOrderAsync(stock, result, orderResult, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStrategyLiveOrderCooldown(key, TimeSpan.FromSeconds(180));
                Dispatcher.Invoke(() => AppendLog($"LIVE BUY ERROR: {stock.Code} {stock.Name} / {result.Name} / {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private async Task AuditStrategyLiveBuyOrderAsync(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            KiwoomOrderResult orderResult,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<KiwoomOpenOrder> openOrders = await _tradingClient
                    .GetOpenOrdersAsync(stock.Code, exchangeType: KiwoomTradingConstants.IntegratedExchangeType, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                IReadOnlyList<KiwoomFill> fills = await _tradingClient
                    .GetFillsAsync(stock.Code, orderResult.OrderNo, exchangeType: KiwoomTradingConstants.IntegratedExchangeType, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                long unfilled = openOrders.Sum(order => Math.Max(0, order.UnfilledQuantity));
                long filled = fills.Sum(fill => Math.Max(0, fill.FilledQuantity));
                StrategyPositionLedgerEntry? positionEntry = SaveStrategyBuyPosition(stock, result, orderResult, fills);

                Dispatcher.Invoke(() =>
                {
                    AppendLog(
                        $"LIVE BUY AUDIT: {stock.Code} {stock.Name} / {result.Name} / " +
                        $"open {openOrders.Count:N0} / unfilled {unfilled:N0} / fills {fills.Count:N0} / filled {filled:N0}");
                    if (positionEntry != null)
                    {
                        AppendReadyLog(
                            $"STRATEGY POSITION TAGGED: {stock.Code} {stock.Name} / {positionEntry.SlotTag} / " +
                            $"qty {positionEntry.Quantity:N0} / avg {positionEntry.AveragePrice:N0} / entry5m low {positionEntry.Entry5MinuteLow:N0}");
                    }
                    SaveStrategyOrderJournal(
                        BuildStrategyLiveBuyOrderKey(stock, result),
                        "BUY",
                        stock,
                        result.SlotId.ToString(),
                        result.Name,
                        0,
                        0,
                        0,
                        orderResult,
                        "AUDITED",
                        $"open {openOrders.Count:N0} / unfilled {unfilled:N0} / fills {fills.Count:N0} / filled {filled:N0}");
                    _ = RefreshBalanceAsync("strategy live buy");
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"LIVE BUY AUDIT ERROR: {stock.Code} {stock.Name} / {result.Name} / {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private StrategyLiveBuyGuardResult EvaluateLiveBuyRiskGuard(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            StrategyExecutionSettings execution)
        {
            if (!execution.AllowsLiveBuy)
                return StrategyLiveBuyGuardResult.Blocked("Live Orders OFF");
            if (!IsLiveStrategyOrderWindowOpen(out string marketReason))
                return StrategyLiveBuyGuardResult.Blocked(marketReason);
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
                return StrategyLiveBuyGuardResult.Blocked("stock missing");
            if (IsStockOwned(stock))
                return StrategyLiveBuyGuardResult.Blocked("already owned");
            if (HasStrategyLiveBuyOrderToday(stock))
                return StrategyLiveBuyGuardResult.Blocked("live buy already submitted today");
            if (!IsStrategyMinuteDataReady(stock))
                return StrategyLiveBuyGuardResult.Blocked("minute ledger not ready");
            if (!result.HasSignal)
                return StrategyLiveBuyGuardResult.Blocked("signal not active");
            if (execution.SlotCount <= 0)
                return StrategyLiveBuyGuardResult.Blocked("slot count missing");
            if (CountStrategyLiveBuyOrdersToday() >= execution.SlotCount)
                return StrategyLiveBuyGuardResult.Blocked($"slot limit reached: {CountStrategyLiveBuyOrdersToday():N0}/{execution.SlotCount:N0}");

            long referencePrice = ResolveStrategySignalPrice(stock);
            if (referencePrice <= 0)
                return StrategyLiveBuyGuardResult.Blocked("reference price missing");

            long slotCount = Math.Max(1, execution.SlotCount);
            long perSlotBudget = execution.Budget / slotCount;
            if (perSlotBudget <= 0)
                return StrategyLiveBuyGuardResult.Blocked("budget missing");

            long quantity = perSlotBudget / referencePrice;
            if (quantity <= 0)
                return StrategyLiveBuyGuardResult.Blocked($"budget too small: {perSlotBudget:N0} / price {referencePrice:N0}");

            return StrategyLiveBuyGuardResult.Allow(quantity, referencePrice);
        }

        private async Task TrySendStrategySignalAlertAsync(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            string orderMode,
            CancellationToken cancellationToken = default)
        {
            if (!ShouldSendStrategySignalTelegram())
                return;

            try
            {
                string message = BuildStrategySignalAlertMessage(stock, result, orderMode);
                await _telegramNotifier.SendHtmlToDefaultAsync(message, cancellationToken).ConfigureAwait(false);
                Dispatcher.Invoke(() => AppendLog($"strategy signal alert sent: {stock.Name} ({stock.Code}) / {result.Name}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Dispatcher.Invoke(() => AppendLog($"strategy signal alert error: {stock.Code} / {ex.Message}"));
            }
        }

        private bool ShouldSendStrategySignalTelegram()
        {
            return _config.Telegram.Enabled &&
                !string.IsNullOrWhiteSpace(_config.Telegram.BotToken) &&
                !string.IsNullOrWhiteSpace(_config.Telegram.DefaultChatId);
        }

        private string BuildStrategySignalAlertKey(WatchStockItem stock, StrategyEvaluationResult result)
        {
            return $"{NormalizeStockCode(stock.Code)}|{result.SlotId}|SIGNAL|{DateTime.Today:yyyyMMdd}";
        }

        private string BuildStrategyLiveBuyOrderKey(WatchStockItem stock, StrategyEvaluationResult result)
        {
            return $"{NormalizeStockCode(stock.Code)}|{result.SlotId}|LIVE_BUY|{DateTime.Today:yyyyMMdd}";
        }

        private bool HasStrategyLiveBuyOrderToday(WatchStockItem stock)
        {
            string prefix = $"{NormalizeStockCode(stock.Code)}|";
            string suffix = $"|LIVE_BUY|{DateTime.Today:yyyyMMdd}";
            lock (_strategyLiveOrderLock)
            {
                return _strategyLiveBuyOrderKeys.Any(key =>
                    key.StartsWith(prefix, StringComparison.Ordinal) &&
                    key.EndsWith(suffix, StringComparison.Ordinal));
            }
        }

        private int CountStrategyLiveBuyOrdersToday()
        {
            string suffix = $"|LIVE_BUY|{DateTime.Today:yyyyMMdd}";
            lock (_strategyLiveOrderLock)
                return _strategyLiveBuyOrderKeys.Count(key => key.EndsWith(suffix, StringComparison.Ordinal));
        }

        private bool ReserveStrategyLiveBuyOrderKey(string key)
        {
            lock (_strategyLiveOrderLock)
                return _strategyLiveBuyOrderKeys.Add(key);
        }

        private void ReleaseStrategyLiveBuyOrderKey(string key)
        {
            lock (_strategyLiveOrderLock)
                _strategyLiveBuyOrderKeys.Remove(key);
        }

        private bool IsStrategyLiveOrderInCooldown(string key)
        {
            lock (_strategyLiveOrderLock)
            {
                return _strategyLiveOrderRetryAfterByKey.TryGetValue(key, out DateTime retryAfter) &&
                    DateTime.Now < retryAfter;
            }
        }

        private void SetStrategyLiveOrderCooldown(string key, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(key) || duration <= TimeSpan.Zero)
                return;

            lock (_strategyLiveOrderLock)
                _strategyLiveOrderRetryAfterByKey[key] = DateTime.Now + duration;
        }

        private void LogStrategyLiveOrderBlockedOnce(string key, string message)
        {
            string logKey = $"{key}|{message}";
            lock (_strategyLiveOrderLock)
            {
                if (_strategyLiveOrderBlockedLogAfterByKey.TryGetValue(logKey, out DateTime nextLogAt) &&
                    DateTime.Now < nextLogAt)
                    return;

                _strategyLiveOrderBlockedLogAfterByKey[logKey] = DateTime.Now + TimeSpan.FromSeconds(60);
            }

            Dispatcher.Invoke(() => AppendLog(message));
        }

        private void SaveStrategyOrderJournal(
            string key,
            string side,
            WatchStockItem stock,
            string slotId,
            string reason,
            long quantity,
            long referencePrice,
            long orderPrice,
            KiwoomOrderResult orderResult,
            string stage,
            string memo = "")
        {
            _strategyOrderJournalStore.UpsertToday(new StrategyOrderJournalEntry
            {
                Key = key,
                Side = side,
                Code = NormalizeStockCode(stock.Code),
                Name = stock.Name,
                SlotId = slotId,
                Reason = reason,
                Quantity = quantity,
                ReferencePrice = referencePrice,
                OrderPrice = orderPrice,
                OrderNo = orderResult.OrderNo,
                ReturnCode = orderResult.ReturnCode,
                ReturnMessage = orderResult.ReturnMessage,
                Success = orderResult.Success,
                Stage = stage,
                Memo = memo
            });
        }

        private static long ResolveStrategySignalPrice(WatchStockItem stock)
        {
            if (stock.CurrentPrice > 0)
                return stock.CurrentPrice;
            return stock.LastPrice;
        }

        private bool IsLiveStrategyOrderWindowOpen(out string reason)
        {
            DateTime now = DateTime.Now;
            if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                reason = "live order blocked: weekend";
                return false;
            }

            if ((now - _lastMarketStatusAt).TotalSeconds <= 180)
            {
                if (string.Equals(_lastMarketStatusCode, "3", StringComparison.OrdinalIgnoreCase) ||
                    IsNxtOpenStatus(_lastMarketStatusCode))
                {
                    reason = "market open";
                    return true;
                }

                reason = $"live order blocked: market status {_lastMarketStatusCode} {_lastMarketStatusText}";
                return false;
            }

            TimeSpan time = now.TimeOfDay;
            bool krxRegularWindow = time >= new TimeSpan(9, 0, 0) && time < new TimeSpan(15, 30, 0);
            if (krxRegularWindow || IsNxtMarketWindow())
            {
                reason = "market time fallback";
                return true;
            }

            reason = "live order blocked: market status stale/outside trading window";
            return false;
        }

        private static string BuildStrategySignalAlertMessage(
            WatchStockItem stock,
            StrategyEvaluationResult result,
            string orderMode)
        {
            string name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(stock.Name) ? stock.Code : stock.Name);
            string code = WebUtility.HtmlEncode(stock.Code);
            string strategy = WebUtility.HtmlEncode(result.Name);
            string mode = WebUtility.HtmlEncode(orderMode);
            string summary = WebUtility.HtmlEncode(result.Summary);
            string price = ResolveStrategySignalPrice(stock) > 0 ? $"{ResolveStrategySignalPrice(stock):N0}" : "-";

            return string.Join(Environment.NewLine, new[]
            {
                $"<b>STRATEGY BUY SIGNAL</b> {name} ({code})",
                $"strategy: {strategy}",
                $"price: {price}",
                $"mode: {mode}",
                $"summary: {summary}"
            });
        }

        private readonly record struct StrategyLiveBuyGuardResult(
            bool Allowed,
            string Reason,
            long Quantity,
            long ReferencePrice)
        {
            public static StrategyLiveBuyGuardResult Allow(long quantity, long referencePrice) =>
                new(true, string.Empty, quantity, referencePrice);

            public static StrategyLiveBuyGuardResult Blocked(string reason) =>
                new(false, reason, 0, 0);
        }
    }
}
