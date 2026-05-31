using System;
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
        private const decimal StrategyStopLossRate = -1.5m;
        private const decimal StrategyFirstTargetRate = 3.0m;

        private void ProcessStrategyExitAlerts(WatchStockItem? stock)
        {
            StrategyExecutionSettings execution = GetStrategyExecutionSettings();
            if (!execution.AutoTradingEnabled ||
                stock == null ||
                string.IsNullOrWhiteSpace(stock.Code))
                return;

            if (!TryResolveStrategyHolding(stock.Code, out KiwoomHolding holding))
                return;

            if (IsManualBuyStopAssistTarget(stock, holding))
            {
                if (!TryGetManualBuyStopAnchor(stock, out ManualBuyStopAnchor anchor))
                    return;

                StrategyExitCheck manualCheck = EvaluateManualBuyStopAssistExitCheck(stock, anchor);
                if (!manualCheck.HasExitSignal)
                    return;

                if (execution.AllowsLiveBuy)
                    _ = TrySubmitStrategyLiveSellAsync(stock, holding, manualCheck);

                string manualKey = BuildStrategyExitAlertKey(stock, manualCheck);
                if (!_strategyExitAlertLoggedKeys.Add(manualKey))
                    return;

                string manualOrderMode = execution.LiveBuyEnabled
                    ? "LIVE ORDERS ON / manual stop sell handoff pending"
                    : "LIVE ORDERS OFF / manual stop alert only";

                AppendReadyLog(
                    $"MANUAL STOP SIGNAL: {stock.Code} {stock.Name} / {manualCheck.Reason} / " +
                    $"price {manualCheck.CurrentPrice:N0} / stop {anchor.EntryLow:N0} / pnl {manualCheck.ProfitRate:0.##}% / {manualOrderMode}");

                _ = TrySendStrategyExitAlertAsync(stock, manualCheck, manualOrderMode);
                return;
            }

            StrategyExitCheck check = EvaluateStrategyExitCheck(stock, holding);
            if (!check.HasExitSignal)
                return;

            if (execution.AllowsLiveBuy)
                _ = TrySubmitStrategyLiveSellAsync(stock, holding, check);

            string key = BuildStrategyExitAlertKey(stock, check);
            if (!_strategyExitAlertLoggedKeys.Add(key))
                return;

            string orderMode = execution.LiveBuyEnabled
                ? "LIVE ORDERS ON / sell handoff pending"
                : "LIVE ORDERS OFF / alert only";

            AppendReadyLog(
                $"STRATEGY EXIT SIGNAL: {stock.Code} {stock.Name} / {check.Reason} / " +
                $"price {check.CurrentPrice:N0} / avg {check.AverageBuyPrice:N0} / pnl {check.ProfitRate:0.##}% / {orderMode}");

            _ = TrySendStrategyExitAlertAsync(stock, check, orderMode);
        }

        private async Task TrySubmitStrategyLiveSellAsync(
            WatchStockItem stock,
            KiwoomHolding holding,
            StrategyExitCheck check,
            CancellationToken cancellationToken = default)
        {
            string key = BuildStrategyLiveSellOrderKey(stock, check);
            if (IsStrategyLiveOrderInCooldown(key))
                return;

            StrategyLiveSellGuardResult guard = EvaluateLiveSellRiskGuard(stock, holding, check);
            if (!guard.Allowed)
            {
                LogStrategyLiveOrderBlockedOnce(key, $"LIVE SELL BLOCKED: {stock.Code} {stock.Name} / {check.Reason} / {guard.Reason}");
                return;
            }

            if (!ReserveStrategyLiveSellOrderKey(key))
                return;

            try
            {
                KiwoomOrderRequest request = KiwoomOrderRequest.SorLimitFromCurrentPrice(
                    stock.Code,
                    guard.Quantity,
                    guard.ReferencePrice,
                    tickOffset: -1);

                KiwoomOrderResult orderResult = await _tradingClient.SellAsync(request, cancellationToken).ConfigureAwait(false);
                if (!orderResult.Success)
                {
                    ReleaseStrategyLiveSellOrderKey(key);
                    SetStrategyLiveOrderCooldown(key, TimeSpan.FromSeconds(60));
                }
                SaveStrategyOrderJournal(
                    key,
                    "SELL",
                    stock,
                    string.Empty,
                    check.Reason,
                    guard.Quantity,
                    guard.ReferencePrice,
                    request.OrderPrice,
                    orderResult,
                    "SUBMITTED");

                Dispatcher.Invoke(() =>
                {
                    string status = orderResult.Success ? "SENT" : "FAILED";
                    AppendReadyLog(
                        $"LIVE SELL {status}: {stock.Code} {stock.Name} / {check.Reason} / " +
                        $"qty {guard.Quantity:N0} / limit {request.OrderPrice:N0} / ref {guard.ReferencePrice:N0} / " +
                        $"order {orderResult.OrderNo} / {orderResult.ReturnCode} {orderResult.ReturnMessage}");
                });

                if (orderResult.Success)
                    await AuditStrategyLiveSellOrderAsync(stock, check, orderResult, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStrategyLiveOrderCooldown(key, TimeSpan.FromSeconds(180));
                Dispatcher.Invoke(() => AppendLog($"LIVE SELL ERROR: {stock.Code} {stock.Name} / {check.Reason} / {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private async Task AuditStrategyLiveSellOrderAsync(
            WatchStockItem stock,
            StrategyExitCheck check,
            KiwoomOrderResult orderResult,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

                var openOrders = await _tradingClient
                    .GetOpenOrdersAsync(stock.Code, exchangeType: KiwoomTradingConstants.IntegratedExchangeType, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var fills = await _tradingClient
                    .GetFillsAsync(stock.Code, orderResult.OrderNo, exchangeType: KiwoomTradingConstants.IntegratedExchangeType, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                long unfilled = openOrders.Sum(order => Math.Max(0, order.UnfilledQuantity));
                long filled = fills.Sum(fill => Math.Max(0, fill.FilledQuantity));

                Dispatcher.Invoke(() =>
                {
                    AppendLog(
                        $"LIVE SELL AUDIT: {stock.Code} {stock.Name} / {check.Reason} / " +
                        $"open {openOrders.Count:N0} / unfilled {unfilled:N0} / fills {fills.Count:N0} / filled {filled:N0}");
                    SaveStrategyOrderJournal(
                        BuildStrategyLiveSellOrderKey(stock, check),
                        "SELL",
                        stock,
                        string.Empty,
                        check.Reason,
                        0,
                        0,
                        0,
                        orderResult,
                        "AUDITED",
                        $"open {openOrders.Count:N0} / unfilled {unfilled:N0} / fills {fills.Count:N0} / filled {filled:N0}");
                    _ = RefreshBalanceAsync("strategy live sell");
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"LIVE SELL AUDIT ERROR: {stock.Code} {stock.Name} / {check.Reason} / {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private StrategyLiveSellGuardResult EvaluateLiveSellRiskGuard(
            WatchStockItem stock,
            KiwoomHolding holding,
            StrategyExitCheck check)
        {
            if (!GetStrategyExecutionSettings().AllowsLiveBuy)
                return StrategyLiveSellGuardResult.Blocked("Live Orders OFF");
            if (!IsLiveStrategyOrderWindowOpen(out string marketReason))
                return StrategyLiveSellGuardResult.Blocked(marketReason);
            if (!check.HasExitSignal)
                return StrategyLiveSellGuardResult.Blocked("exit signal not active");
            if (HasStrategyLiveSellOrderToday(stock, check))
                return StrategyLiveSellGuardResult.Blocked($"{check.Reason} live sell already submitted today");

            long orderableQuantity = holding.OrderableQuantity > 0
                ? holding.OrderableQuantity
                : holding.HoldingQuantity;
            long quantity = string.Equals(check.Reason, "TARGET1", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, orderableQuantity / 2)
                : orderableQuantity;
            if (quantity <= 0)
                return StrategyLiveSellGuardResult.Blocked("sellable quantity missing");
            if (check.CurrentPrice <= 0)
                return StrategyLiveSellGuardResult.Blocked("reference price missing");

            return StrategyLiveSellGuardResult.Allow(quantity, check.CurrentPrice);
        }

        private bool TryResolveStrategyHolding(string code, out KiwoomHolding holding)
        {
            string normalizedCode = NormalizeStockCode(code);
            foreach (KiwoomHolding item in _balanceHoldings)
            {
                if (item.HoldingQuantity > 0 &&
                    string.Equals(NormalizeStockCode(item.StockCode), normalizedCode, StringComparison.Ordinal))
                {
                    holding = item;
                    return true;
                }
            }

            holding = new KiwoomHolding(string.Empty, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0);
            return false;
        }

        private static StrategyExitCheck EvaluateStrategyExitCheck(WatchStockItem stock, KiwoomHolding holding)
        {
            long currentPrice = ResolveStrategySignalPrice(stock);
            long averageBuyPrice = holding.AverageBuyPrice;
            if (currentPrice <= 0 || averageBuyPrice <= 0)
                return StrategyExitCheck.None(currentPrice, averageBuyPrice);

            decimal profitRate = (currentPrice - averageBuyPrice) / (decimal)averageBuyPrice * 100m;
            if (profitRate <= StrategyStopLossRate)
                return StrategyExitCheck.Signal("STOP", currentPrice, averageBuyPrice, profitRate);
            if (profitRate >= StrategyFirstTargetRate)
                return StrategyExitCheck.Signal("TARGET1", currentPrice, averageBuyPrice, profitRate);

            return StrategyExitCheck.None(currentPrice, averageBuyPrice, profitRate);
        }

        private bool IsManualBuyStopAssistTarget(WatchStockItem stock, KiwoomHolding holding)
        {
            return IsManualBuyStopAssistEnabled() &&
                holding.HoldingQuantity > 0 &&
                !HasAutomaticStrategyPositionToday(stock.Code);
        }

        private bool IsManualBuyStopAssistEnabled() =>
            StrategySlotThemeAssistToggle != null && IsStrategyToggleOn(StrategySlotThemeAssistToggle);

        private bool TryGetManualBuyStopAnchor(WatchStockItem stock, out ManualBuyStopAnchor anchor)
        {
            string code = NormalizeStockCode(stock.Code);
            if (_manualBuyStopAnchorsByCode.TryGetValue(code, out anchor))
                return true;

            if (_manualBuyStopAnchorLoadingCodes.Add(code))
                _ = TryLoadManualBuyStopAnchorAsync(stock);

            anchor = default;
            return false;
        }

        private async Task TryLoadManualBuyStopAnchorAsync(WatchStockItem stock, CancellationToken cancellationToken = default)
        {
            string code = NormalizeStockCode(stock.Code);
            try
            {
                IReadOnlyList<KiwoomFill> fills = await _tradingClient
                    .GetFillsAsync(stock.Code, exchangeType: KiwoomTradingConstants.IntegratedExchangeType, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                KiwoomFill? latestBuyFill = fills
                    .Where(fill => fill.FilledQuantity > 0 &&
                        string.Equals(NormalizeStockCode(fill.StockCode), code, StringComparison.Ordinal) &&
                        IsBuyFill(fill))
                    .OrderByDescending(fill => ParseFillTime(fill.OrderTime))
                    .FirstOrDefault();

                DateTime fillTime = latestBuyFill == null ? DateTime.MinValue : ParseFillTime(latestBuyFill.OrderTime);
                string market = ShouldUseNxtDataForStock(stock.Code) ? "NXT" : "KRX";
                if (fillTime == DateTime.MinValue ||
                    !_strategyMinuteCacheService.TryGetBarAt(code, market, 5, fillTime, out StrategyMinuteBar entryBar) ||
                    entryBar.Low <= 0)
                {
                    Dispatcher.Invoke(() => AppendLog($"manual buy stop anchor wait: {stock.Code} {stock.Name} / fill or 5m bar missing"));
                    return;
                }

                var anchor = new ManualBuyStopAnchor(code, entryBar.BucketTime, entryBar.Low, fillTime, DateTime.Now);
                _manualBuyStopAnchorsByCode[code] = anchor;
                Dispatcher.Invoke(() => AppendLog($"manual buy stop anchor set: {stock.Code} {stock.Name} / fill {fillTime:HH:mm:ss} / 5m {entryBar.BucketTime:HH:mm} / low {entryBar.Low:N0}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Dispatcher.Invoke(() => AppendLog($"manual buy stop anchor error: {stock.Code} / {ex.GetType().Name}: {ex.Message}"));
            }
            finally
            {
                _manualBuyStopAnchorLoadingCodes.Remove(code);
            }
        }

        private static bool IsBuyFill(KiwoomFill fill)
        {
            string side = fill.OrderSideText ?? string.Empty;
            return side.Contains("매수", StringComparison.OrdinalIgnoreCase) ||
                side.Contains("BUY", StringComparison.OrdinalIgnoreCase) ||
                side.StartsWith("+", StringComparison.Ordinal);
        }

        private static DateTime ParseFillTime(string value)
        {
            string digits = new([.. (value ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length < 6)
                return DateTime.MinValue;

            int hour = int.Parse(digits[..2]);
            int minute = int.Parse(digits.Substring(2, 2));
            int second = int.Parse(digits.Substring(4, 2));
            if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
                return DateTime.MinValue;

            DateTime today = DateTime.Today;
            return new DateTime(today.Year, today.Month, today.Day, hour, minute, second);
        }

        private StrategyExitCheck EvaluateManualBuyStopAssistExitCheck(WatchStockItem stock, ManualBuyStopAnchor anchor)
        {
            long currentPrice = ResolveStrategySignalPrice(stock);
            StrategyMinuteFrameSnapshot? frame = BuildStrategyMinuteSnapshotSet(stock)?.Get(5);
            if (currentPrice <= 0 || frame == null)
                return StrategyExitCheck.None(currentPrice, anchor.EntryLow);

            bool entryLowBreak = anchor.EntryLow > 0 && currentPrice <= anchor.EntryLow;
            bool ma5Break = IsManualBuyStopMa5Breakdown(frame);
            if (!entryLowBreak && !ma5Break)
                return StrategyExitCheck.None(currentPrice, anchor.EntryLow, CalculateProfitRate(currentPrice, anchor.EntryLow));

            string reason = entryLowBreak
                ? "MANUAL_STOP_ENTRY_LOW"
                : "MANUAL_STOP_5M_MA5";
            return StrategyExitCheck.Signal(reason, currentPrice, anchor.EntryLow, CalculateProfitRate(currentPrice, anchor.EntryLow));
        }

        private static bool IsManualBuyStopMa5Breakdown(StrategyMinuteFrameSnapshot frame)
        {
            double line5 = frame.Ma5 > 0 ? frame.Ma5 : frame.LastCompletedMa5;
            if (line5 <= 0 ||
                frame.LastCompletedOpen <= 0 ||
                frame.LastCompletedHigh <= 0 ||
                frame.LastCompletedClose <= 0 ||
                frame.CurrentOpen <= 0 ||
                frame.CurrentHigh <= 0 ||
                frame.CurrentLow <= 0 ||
                frame.CurrentClose <= 0)
                return false;

            double prevBodyHigh = Math.Max(frame.LastCompletedClose, frame.LastCompletedOpen);
            double prevRealHigh = prevBodyHigh + (frame.LastCompletedHigh - prevBodyHigh) * 0.1d;
            bool prevAbove = prevRealHigh > line5;

            bool currBear = frame.CurrentClose < frame.CurrentOpen;
            bool deadCross = frame.CurrentClose < line5 && frame.CurrentOpen >= line5;

            long body = frame.CurrentOpen - frame.CurrentClose;
            long tail = frame.CurrentClose - frame.CurrentLow;
            long range = frame.CurrentHigh - frame.CurrentLow;
            bool downBreak = body > 0 &&
                range > 0 &&
                tail <= body * 0.15m &&
                frame.CurrentClose <= frame.CurrentLow + range * 0.05m;

            return prevAbove && currBear && deadCross && downBreak;
        }

        private static decimal CalculateProfitRate(long currentPrice, long basePrice)
        {
            if (currentPrice <= 0 || basePrice <= 0)
                return 0;

            return (currentPrice - basePrice) / (decimal)basePrice * 100m;
        }

        private async Task TrySendStrategyExitAlertAsync(
            WatchStockItem stock,
            StrategyExitCheck check,
            string orderMode,
            CancellationToken cancellationToken = default)
        {
            if (!ShouldSendStrategySignalTelegram())
                return;

            try
            {
                string message = BuildStrategyExitAlertMessage(stock, check, orderMode);
                await _telegramNotifier.SendHtmlToDefaultAsync(message, cancellationToken).ConfigureAwait(false);
                Dispatcher.Invoke(() => AppendLog($"strategy exit alert sent: {stock.Name} ({stock.Code}) / {check.Reason}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Dispatcher.Invoke(() => AppendLog($"strategy exit alert error: {stock.Code} / {ex.Message}"));
            }
        }

        private string BuildStrategyExitAlertKey(WatchStockItem stock, StrategyExitCheck check)
        {
            return $"{NormalizeStockCode(stock.Code)}|EXIT|{check.Reason}|{DateTime.Today:yyyyMMdd}";
        }

        private string BuildStrategyLiveSellOrderKey(WatchStockItem stock, StrategyExitCheck check)
        {
            return $"{NormalizeStockCode(stock.Code)}|LIVE_SELL|{check.Reason}|{DateTime.Today:yyyyMMdd}";
        }

        private bool HasStrategyLiveSellOrderToday(WatchStockItem stock, StrategyExitCheck check)
        {
            lock (_strategyLiveOrderLock)
                return _strategyLiveSellOrderKeys.Contains(BuildStrategyLiveSellOrderKey(stock, check));
        }

        private bool ReserveStrategyLiveSellOrderKey(string key)
        {
            lock (_strategyLiveOrderLock)
                return _strategyLiveSellOrderKeys.Add(key);
        }

        private void ReleaseStrategyLiveSellOrderKey(string key)
        {
            lock (_strategyLiveOrderLock)
                _strategyLiveSellOrderKeys.Remove(key);
        }

        private static string BuildStrategyExitAlertMessage(
            WatchStockItem stock,
            StrategyExitCheck check,
            string orderMode)
        {
            string name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(stock.Name) ? stock.Code : stock.Name);
            string code = WebUtility.HtmlEncode(stock.Code);
            string reason = WebUtility.HtmlEncode(check.Reason);
            string mode = WebUtility.HtmlEncode(orderMode);

            return string.Join(Environment.NewLine, new[]
            {
                $"<b>STRATEGY EXIT SIGNAL</b> {name} ({code})",
                $"reason: {reason}",
                $"price: {check.CurrentPrice:N0} / avg: {check.AverageBuyPrice:N0}",
                $"pnl: {check.ProfitRate:0.##}%",
                $"mode: {mode}"
            });
        }

        private readonly record struct StrategyExitCheck(
            bool HasExitSignal,
            string Reason,
            long CurrentPrice,
            long AverageBuyPrice,
            decimal ProfitRate)
        {
            public static StrategyExitCheck Signal(string reason, long currentPrice, long averageBuyPrice, decimal profitRate) =>
                new(true, reason, currentPrice, averageBuyPrice, profitRate);

            public static StrategyExitCheck None(long currentPrice, long averageBuyPrice, decimal profitRate = 0) =>
                new(false, string.Empty, currentPrice, averageBuyPrice, profitRate);
        }

        private readonly record struct StrategyLiveSellGuardResult(
            bool Allowed,
            string Reason,
            long Quantity,
            long ReferencePrice)
        {
            public static StrategyLiveSellGuardResult Allow(long quantity, long referencePrice) =>
                new(true, string.Empty, quantity, referencePrice);

            public static StrategyLiveSellGuardResult Blocked(string reason) =>
                new(false, reason, 0, 0);
        }
    }
}
