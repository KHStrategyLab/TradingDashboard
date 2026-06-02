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
        private void ProcessStrategyExitAlerts(WatchStockItem? stock)
        {
            StrategyExecutionSettings execution = GetStrategyExecutionSettings();
            if (!execution.AutoTradingEnabled ||
                stock == null ||
                string.IsNullOrWhiteSpace(stock.Code))
                return;

            if (!TryResolveStrategyHolding(stock.Code, out KiwoomHolding holding))
                return;

            if (ProcessStrategyPositionExitAlerts(stock, holding, execution))
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

            // 매도/손절 알림은 자동 전략 장부 또는 Manual Buy Stop Assist가 명시적으로 소유한 포지션만 처리한다.
            // 보유 종목 전체를 평균단가 기준으로 훑는 fallback은 꺼진 4번 손절기까지 알림을 내는 원인이 되므로 금지한다.
        }

        private bool ProcessStrategyPositionExitAlerts(
            WatchStockItem stock,
            KiwoomHolding holding,
            StrategyExecutionSettings execution)
        {
            var positions = _strategyPositionLedgerByKey.Values
                .Where(entry => string.Equals(entry.Code, NormalizeStockCode(stock.Code), StringComparison.Ordinal) &&
                    string.Equals(entry.Source, "AUTO", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                    entry.OpenQuantity > 0)
                .ToList();

            if (positions.Count == 0)
                return false;

            foreach (StrategyPositionLedgerEntry position in positions)
            {
                StrategyExitCheck check = EvaluatePositionExitCheck(stock, position);
                if (!check.HasExitSignal)
                    continue;

                if (execution.AllowsLiveBuy)
                    _ = TrySubmitStrategyLiveSellAsync(stock, holding, check);

                string key = BuildStrategyExitAlertKey(stock, check);
                if (!_strategyExitAlertLoggedKeys.Add(key))
                    continue;

                string orderMode = execution.LiveBuyEnabled
                    ? "LIVE ORDERS ON / strategy-position sell handoff pending"
                    : "LIVE ORDERS OFF / strategy-position alert only";

                AppendReadyLog(
                    $"STRATEGY EXIT SIGNAL: {stock.Code} {stock.Name} / {check.SlotTag} / {check.Reason} / " +
                    $"price {check.CurrentPrice:N0} / entry {check.AverageBuyPrice:N0} / qty {check.Quantity:N0} / pnl {check.ProfitRate:0.##}% / {orderMode}");

                _ = TrySendStrategyExitAlertAsync(stock, check, orderMode);
            }

            return true;
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

            if (!ReserveStrategyLiveSellOrderKey(key, guard.Quantity))
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
                    check.SlotTag,
                    check.Reason,
                    guard.Quantity,
                    guard.ReferencePrice,
                    request.OrderPrice,
                    orderResult,
                    "SUBMITTED",
                    exitStrategyCode: check.ExitStrategyCode);

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

                long unfilled = openOrders
                    .Where(order => string.Equals(order.OrderNo, orderResult.OrderNo, StringComparison.Ordinal))
                    .Sum(order => Math.Max(0, order.UnfilledQuantity));
                long filled = fills.Sum(fill => Math.Max(0, fill.FilledQuantity));
                UpdateStrategyLiveSellReservation(BuildStrategyLiveSellOrderKey(stock, check), unfilled);

                Dispatcher.Invoke(() =>
                {
                    AppendLog(
                        $"LIVE SELL AUDIT: {stock.Code} {stock.Name} / {check.Reason} / " +
                        $"open {openOrders.Count:N0} / unfilled {unfilled:N0} / fills {fills.Count:N0} / filled {filled:N0}");
                    ApplyStrategyPositionSellFill(check, filled);
                    ApplyManualPositionSellFill(check, filled);
                    SaveStrategyOrderJournal(
                        BuildStrategyLiveSellOrderKey(stock, check),
                        "SELL",
                        stock,
                        check.SlotTag,
                        check.Reason,
                        filled,
                        0,
                        0,
                        orderResult,
                        "AUDITED",
                        $"open {openOrders.Count:N0} / unfilled {unfilled:N0} / fills {fills.Count:N0} / filled {filled:N0}",
                        exitStrategyCode: check.ExitStrategyCode);
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
            long reservedQuantity = GetStrategyLiveSellReservedQuantity(stock.Code);
            long availableQuantity = Math.Max(0, orderableQuantity - reservedQuantity);
            if (availableQuantity <= 0)
                return StrategyLiveSellGuardResult.Blocked($"sellable quantity missing: orderable {orderableQuantity:N0} / reserved {reservedQuantity:N0}");

            long decisionQuantity = check.Quantity > 0 ? check.Quantity : orderableQuantity;
            long quantity = string.Equals(check.Reason, "TARGET1", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, Math.Min(decisionQuantity, availableQuantity))
                : Math.Min(decisionQuantity, availableQuantity);
            if (quantity <= 0)
                return StrategyLiveSellGuardResult.Blocked($"sellable quantity missing: orderable {orderableQuantity:N0} / reserved {reservedQuantity:N0}");
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

            StrategyExitCheck check = EvaluateExitDecision(currentPrice, averageBuyPrice, 0, string.Empty, string.Empty);
            if (check.HasExitSignal)
                return check;

            return StrategyExitCheck.None(currentPrice, averageBuyPrice, CalculateProfitRate(currentPrice, averageBuyPrice));
        }

        private static StrategyExitCheck EvaluatePositionExitCheck(WatchStockItem stock, StrategyPositionLedgerEntry position)
        {
            long currentPrice = ResolveStrategySignalPrice(stock);
            if (currentPrice > 0 && position.AveragePrice > 0 && position.Entry5MinuteLow > 0 && currentPrice <= position.Entry5MinuteLow)
            {
                return StrategyExitCheck.Signal(
                    "ENTRY_5M_LOW_STOP",
                    currentPrice,
                    position.AveragePrice,
                    CalculateProfitRate(currentPrice, position.AveragePrice),
                    position.Key,
                    position.SlotTag,
                    position.OpenQuantity,
                    position.ExitStrategyCode);
            }

            DateTime entryTime = ParseLedgerTime(position.FillTime);
            return EvaluateExitDecision(
                currentPrice,
                position.AveragePrice,
                position.OpenQuantity,
                position.Key,
                position.SlotTag,
                position.ExitStrategyCode,
                entryTime);
        }

        private static StrategyExitCheck EvaluateExitDecision(
            long currentPrice,
            long entryPrice,
            long quantity,
            string positionKey,
            string slotTag,
            string exitStrategyCode = "",
            DateTime entryTime = default)
        {
            if (currentPrice <= 0 || entryPrice <= 0)
                return StrategyExitCheck.None(currentPrice, entryPrice);

            decimal profitRate = CalculateProfitRate(currentPrice, entryPrice);
            string code = StrategyExitStrategyRegistry.Resolve(exitStrategyCode).Code;
            return code switch
            {
                StrategyExitStrategyRegistry.SimplePlus6Minus2 => EvaluateThresholdExit(
                    currentPrice,
                    entryPrice,
                    profitRate,
                    positionKey,
                    slotTag,
                    quantity,
                    code,
                    stopRate: -2.0m,
                    targetRate: 6.0m,
                    stopReason: "STOP_MINUS2",
                    targetReason: "TARGET_PLUS6"),
                StrategyExitStrategyRegistry.QuickReactionReentry => EvaluateQuickReactionExit(
                    currentPrice,
                    entryPrice,
                    profitRate,
                    positionKey,
                    slotTag,
                    quantity,
                    code,
                    entryTime),
                StrategyExitStrategyRegistry.ProfitScaleTrail => EvaluateScaleExit(
                    currentPrice,
                    entryPrice,
                    profitRate,
                    positionKey,
                    slotTag,
                    quantity,
                    code,
                    stopRate: -2.0m,
                    firstTargetRate: 3.0m,
                    finalTargetRate: 6.0m),
                StrategyExitStrategyRegistry.SwingHold => EvaluateThresholdExit(
                    currentPrice,
                    entryPrice,
                    profitRate,
                    positionKey,
                    slotTag,
                    quantity,
                    code,
                    stopRate: -4.0m,
                    targetRate: 10.0m,
                    stopReason: "SWING_STOP",
                    targetReason: "SWING_TARGET"),
                _ => EvaluateScaleExit(
                    currentPrice,
                    entryPrice,
                    profitRate,
                    positionKey,
                    slotTag,
                    quantity,
                    code,
                    stopRate: -2.0m,
                    firstTargetRate: 2.0m,
                    finalTargetRate: 4.0m)
            };
        }

        private static StrategyExitCheck EvaluateQuickReactionExit(
            long currentPrice,
            long entryPrice,
            decimal profitRate,
            string positionKey,
            string slotTag,
            long quantity,
            string exitStrategyCode,
            DateTime entryTime)
        {
            if (profitRate <= -2.0m)
                return StrategyExitCheck.Signal("QUICK_STOP", currentPrice, entryPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);

            if (entryTime != default &&
                DateTime.Now - entryTime >= TimeSpan.FromMinutes(5) &&
                profitRate <= 0)
            {
                return StrategyExitCheck.Signal("QUICK_NO_GO", currentPrice, entryPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);
            }

            return EvaluateScaleExit(
                currentPrice,
                entryPrice,
                profitRate,
                positionKey,
                slotTag,
                quantity,
                exitStrategyCode,
                stopRate: -2.0m,
                firstTargetRate: 3.0m,
                finalTargetRate: 6.0m);
        }

        private static StrategyExitCheck EvaluateScaleExit(
            long currentPrice,
            long entryPrice,
            decimal profitRate,
            string positionKey,
            string slotTag,
            long quantity,
            string exitStrategyCode,
            decimal stopRate,
            decimal firstTargetRate,
            decimal finalTargetRate)
        {
            if (profitRate <= stopRate)
                return StrategyExitCheck.Signal("STOP", currentPrice, entryPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);
            if (profitRate >= finalTargetRate)
                return StrategyExitCheck.Signal("TARGET_FINAL", currentPrice, entryPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);
            if (profitRate >= firstTargetRate)
            {
                long targetQuantity = quantity > 1 ? Math.Max(1, quantity / 2) : quantity;
                return StrategyExitCheck.Signal("TARGET1", currentPrice, entryPrice, profitRate, positionKey, slotTag, targetQuantity, exitStrategyCode);
            }

            return StrategyExitCheck.None(currentPrice, entryPrice, profitRate);
        }

        private static StrategyExitCheck EvaluateThresholdExit(
            long currentPrice,
            long entryPrice,
            decimal profitRate,
            string positionKey,
            string slotTag,
            long quantity,
            string exitStrategyCode,
            decimal stopRate,
            decimal targetRate,
            string stopReason,
            string targetReason)
        {
            if (profitRate <= stopRate)
                return StrategyExitCheck.Signal(stopReason, currentPrice, entryPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);
            if (profitRate >= targetRate)
                return StrategyExitCheck.Signal(targetReason, currentPrice, entryPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);

            return StrategyExitCheck.None(currentPrice, entryPrice, profitRate);
        }
        private void ApplyStrategyPositionSellFill(StrategyExitCheck check, long filledQuantity)
        {
            if (filledQuantity <= 0 ||
                string.IsNullOrWhiteSpace(check.PositionKey) ||
                !_strategyPositionLedgerByKey.TryGetValue(check.PositionKey, out StrategyPositionLedgerEntry? position))
                return;

            long sold = Math.Min(filledQuantity, Math.Max(0, position.OpenQuantity));
            position.OpenQuantity = Math.Max(0, position.OpenQuantity - sold);
            if (position.OpenQuantity <= 0)
                position.Status = "CLOSED";

            string fillMemo = position.OpenQuantity <= 0
                ? check.Reason
                : $"{check.Reason} PARTIAL {sold:N0}";
            position.Memo = string.IsNullOrWhiteSpace(position.Memo)
                ? fillMemo
                : $"{position.Memo} / {fillMemo}";
            _strategyPositionLedgerStore.UpsertToday(position);
            _strategyPositionLedgerByKey[position.Key] = position;
        }

        private bool IsManualBuyStopAssistTarget(WatchStockItem stock, KiwoomHolding holding)
        {
            return IsManualBuyStopAssistEnabled() &&
                holding.HoldingQuantity > 0 &&
                !HasAutomaticStrategyPositionToday(stock.Code) &&
                IsManualPositionAssistOpen(stock.Code);
        }

        private bool IsManualBuyStopAssistEnabled() =>
            StrategySlotThemeAssistToggle != null && IsStrategyToggleOn(StrategySlotThemeAssistToggle);

        private bool TryGetManualBuyStopAnchor(WatchStockItem stock, out ManualBuyStopAnchor anchor)
        {
            string code = NormalizeStockCode(stock.Code);
            if (_manualBuyStopAnchorsByCode.TryGetValue(code, out anchor))
                return true;

            if (TryResolveManualPosition(code, out ManualPositionLedgerEntry manualPosition) &&
                manualPosition.Entry5MinuteLow > 0)
            {
                DateTime entryBarTime = ParseLedgerTime(manualPosition.Entry5MinuteTime);
                DateTime fillTime = ParseLedgerTime(manualPosition.FillTime);
                anchor = new ManualBuyStopAnchor(code, entryBarTime, manualPosition.Entry5MinuteLow, fillTime, DateTime.Now);
                _manualBuyStopAnchorsByCode[code] = anchor;
                return true;
            }

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
                ApplyManualPositionAnchor(stock, anchor);
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

        private static DateTime ParseLedgerTime(string value)
        {
            string digits = new([.. (value ?? string.Empty).Where(char.IsDigit)]);
            if (digits.Length >= 14 &&
                int.TryParse(digits[..4], out int year) &&
                int.TryParse(digits.Substring(4, 2), out int month) &&
                int.TryParse(digits.Substring(6, 2), out int day) &&
                int.TryParse(digits.Substring(8, 2), out int hour) &&
                int.TryParse(digits.Substring(10, 2), out int minute) &&
                int.TryParse(digits.Substring(12, 2), out int second))
            {
                try
                {
                    return new DateTime(year, month, day, hour, minute, second);
                }
                catch
                {
                    return DateTime.MinValue;
                }
            }

            return ParseFillTime(value ?? string.Empty);
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
            long quantity = 0;
            string positionKey = string.Empty;
            if (TryResolveManualPosition(stock.Code, out ManualPositionLedgerEntry manualPosition))
            {
                quantity = manualPosition.OpenQuantity;
                positionKey = manualPosition.Key;
            }

            return StrategyExitCheck.Signal(
                reason,
                currentPrice,
                anchor.EntryLow,
                CalculateProfitRate(currentPrice, anchor.EntryLow),
                positionKey,
                "MANUAL",
                quantity,
                StrategyExitStrategyRegistry.ManualBuyStopAssist);
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
            string owner = string.IsNullOrWhiteSpace(check.PositionKey) ? check.SlotTag : check.PositionKey;
            return $"{NormalizeStockCode(stock.Code)}|EXIT|{owner}|{check.ExitStrategyCode}|{check.Reason}|{DateTime.Today:yyyyMMdd}";
        }

        private string BuildStrategyLiveSellOrderKey(WatchStockItem stock, StrategyExitCheck check)
        {
            string owner = string.IsNullOrWhiteSpace(check.PositionKey) ? check.SlotTag : check.PositionKey;
            return $"{NormalizeStockCode(stock.Code)}|LIVE_SELL|{owner}|{check.ExitStrategyCode}|{check.Reason}|{DateTime.Today:yyyyMMdd}";
        }

        private bool HasStrategyLiveSellOrderToday(WatchStockItem stock, StrategyExitCheck check)
        {
            lock (_strategyLiveOrderLock)
                return _strategyLiveSellOrderKeys.Contains(BuildStrategyLiveSellOrderKey(stock, check));
        }

        private bool ReserveStrategyLiveSellOrderKey(string key, long quantity)
        {
            lock (_strategyLiveOrderLock)
            {
                if (!_strategyLiveSellOrderKeys.Add(key))
                    return false;

                if (quantity > 0)
                    _strategyLiveSellReservedQuantityByKey[key] = quantity;

                return true;
            }
        }

        private void ReleaseStrategyLiveSellOrderKey(string key)
        {
            lock (_strategyLiveOrderLock)
            {
                _strategyLiveSellOrderKeys.Remove(key);
                _strategyLiveSellReservedQuantityByKey.Remove(key);
            }
        }

        private long GetStrategyLiveSellReservedQuantity(string code)
        {
            string normalizedCode = NormalizeStockCode(code);
            string prefix = $"{normalizedCode}|LIVE_SELL|";
            lock (_strategyLiveOrderLock)
            {
                return _strategyLiveSellReservedQuantityByKey
                    .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Sum(item => Math.Max(0, item.Value));
            }
        }

        private void UpdateStrategyLiveSellReservation(string key, long unfilledQuantity)
        {
            lock (_strategyLiveOrderLock)
            {
                if (unfilledQuantity > 0)
                    _strategyLiveSellReservedQuantityByKey[key] = unfilledQuantity;
                else
                    _strategyLiveSellReservedQuantityByKey.Remove(key);
            }
        }

        private static string BuildStrategyExitAlertMessage(
            WatchStockItem stock,
            StrategyExitCheck check,
            string orderMode)
        {
            string name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(stock.Name) ? stock.Code : stock.Name);
            string code = WebUtility.HtmlEncode(stock.Code);
            string reason = WebUtility.HtmlEncode(check.Reason);
            string exitStrategy = WebUtility.HtmlEncode(StrategyExitStrategyRegistry.Resolve(check.ExitStrategyCode).Name);
            string mode = WebUtility.HtmlEncode(orderMode);

            return string.Join(Environment.NewLine, new[]
            {
                $"<b>STRATEGY EXIT SIGNAL</b> {name} ({code})",
                $"reason: {reason}",
                $"exit strategy: {exitStrategy}",
                $"price: {check.CurrentPrice:N0} / avg: {check.AverageBuyPrice:N0}",
                $"slot: {WebUtility.HtmlEncode(check.SlotTag)} / qty: {check.Quantity:N0}",
                $"pnl: {check.ProfitRate:0.##}%",
                $"mode: {mode}"
            });
        }

        private readonly record struct StrategyExitCheck(
            bool HasExitSignal,
            string Reason,
            long CurrentPrice,
            long AverageBuyPrice,
            decimal ProfitRate,
            string PositionKey,
            string SlotTag,
            long Quantity,
            string ExitStrategyCode)
        {
            public static StrategyExitCheck Signal(
                string reason,
                long currentPrice,
                long averageBuyPrice,
                decimal profitRate,
                string positionKey = "",
                string slotTag = "",
                long quantity = 0,
                string exitStrategyCode = "") =>
                new(true, reason, currentPrice, averageBuyPrice, profitRate, positionKey, slotTag, quantity, exitStrategyCode);

            public static StrategyExitCheck None(long currentPrice, long averageBuyPrice, decimal profitRate = 0) =>
                new(false, string.Empty, currentPrice, averageBuyPrice, profitRate, string.Empty, string.Empty, 0, string.Empty);
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
