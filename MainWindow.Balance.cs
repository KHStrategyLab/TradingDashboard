using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TradingDashboard.Services.Trading;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _balanceRequestCts;
        private Point? _balanceGridDragStart;
        private double _balanceGridDragStartOffset;

        private async void BalanceRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshBalanceAsync("manual");
        }

        private async void BalanceStatusText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
                return;

            e.Handled = true;
            await VerifyBalanceAgainstMtsAsync();
        }

        private void BalanceHoldingsDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                return;

            ScrollViewer? scrollViewer = GetBalanceHoldingsScrollViewer();
            if (scrollViewer is null)
                return;

            double direction = e.Delta > 0 ? -1 : 1;
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + direction * 90);
            e.Handled = true;
        }

        private void BalanceHoldingsDataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;

            ScrollViewer? scrollViewer = GetBalanceHoldingsScrollViewer();
            if (scrollViewer is null)
                return;

            _balanceGridDragStart = e.GetPosition(BalanceHoldingsHorizontalScrollViewer);
            _balanceGridDragStartOffset = scrollViewer.HorizontalOffset;
            BalanceHoldingsHorizontalScrollViewer.CaptureMouse();
            BalanceHoldingsHorizontalScrollViewer.Cursor = Cursors.SizeWE;
            e.Handled = true;
        }

        private void BalanceHoldingsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_balanceGridDragStart is not Point dragStart)
                return;

            ScrollViewer? scrollViewer = GetBalanceHoldingsScrollViewer();
            if (scrollViewer is null)
                return;

            Point current = e.GetPosition(BalanceHoldingsHorizontalScrollViewer);
            scrollViewer.ScrollToHorizontalOffset(_balanceGridDragStartOffset - (current.X - dragStart.X));
            e.Handled = true;
        }

        private void BalanceHoldingsDataGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || _balanceGridDragStart is null)
                return;

            _balanceGridDragStart = null;
            BalanceHoldingsHorizontalScrollViewer.ReleaseMouseCapture();
            BalanceHoldingsHorizontalScrollViewer.Cursor = null;
            e.Handled = true;
        }

        private async Task RefreshBalanceAsync(string reason)
        {
            _balanceRequestCts?.Cancel();
            _balanceRequestCts?.Dispose();
            _balanceRequestCts = new CancellationTokenSource();
            CancellationToken cancellationToken = _balanceRequestCts.Token;

            BalanceRefreshButton.IsEnabled = false;
            BalanceStatusText.Text = $"Balance loading... {reason}";

            try
            {
                KiwoomBalanceSnapshot snapshot = await _tradingClient
                    .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketKrx, cancellationToken)
                    .ConfigureAwait(true);

                _balanceHoldings.Clear();
                foreach (KiwoomHolding holding in snapshot.Holdings.OrderByDescending(x => Math.Abs(x.EvaluationAmount)))
                    _balanceHoldings.Add(holding);
                UpdateStrategyProgressRows();

                BalanceTotalPurchaseText.Text = $"{snapshot.TotalPurchaseAmount:N0}";
                BalanceTotalEvaluationText.Text = $"{snapshot.TotalEvaluationAmount:N0}";
                BalanceTotalProfitText.Text = $"{snapshot.TotalEvaluationProfit:N0}";
                BalanceTotalProfitRateText.Text = $"{snapshot.TotalProfitRate:N2}%";
                await RefreshRealizedProfitAsync(cancellationToken).ConfigureAwait(true);
                BalanceStatusText.Text = $"kt00018 {snapshot.QueryMarket} / {snapshot.Holdings.Count} items / {snapshot.CapturedAt:HH:mm:ss}";
                AppendLog($"balance refreshed: {snapshot.SourceApi} / {snapshot.QueryMarket} / {snapshot.Holdings.Count}items / {reason}");
            }
            catch (OperationCanceledException)
            {
                BalanceStatusText.Text = "Balance refresh canceled";
            }
            catch (Exception ex)
            {
                BalanceStatusText.Text = "Balance refresh failed";
                AppendLog($"balance refresh error: {ex.GetType().Name} / {ex.Message}");
            }
            finally
            {
                BalanceRefreshButton.IsEnabled = true;
            }
        }

        private async Task RefreshRealizedProfitAsync(CancellationToken cancellationToken)
        {
            try
            {
                KiwoomRealizedProfitSnapshot snapshot = await _tradingClient
                    .GetTodayRealizedProfitAsync(cancellationToken)
                    .ConfigureAwait(true);

                BalanceRealizedProfitText.Text = FormatSignedNumber(snapshot.RealizedProfit);
                BalanceRealizedProfitText.Foreground = snapshot.RealizedProfit > 0
                    ? _upColorBrush
                    : snapshot.RealizedProfit < 0 ? _downColorBrush : _whiteBrush;

                AppendLog($"realized profit refreshed: ka10074 / {snapshot.QueryDate:yyyyMMdd} / pl {snapshot.RealizedProfit:N0} / fee {snapshot.TradeCommission:N0} / tax {snapshot.TradeTax:N0}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                BalanceRealizedProfitText.Text = "-";
                BalanceRealizedProfitText.Foreground = _whiteBrush;
                AppendLog($"realized profit refresh error: {ex.GetType().Name} / {ex.Message}");
            }
        }

        private static string FormatSignedNumber(long value)
        {
            if (value > 0)
                return $"+{value:N0}";

            return value < 0 ? $"-{Math.Abs(value):N0}" : "0";
        }

        private ScrollViewer? GetBalanceHoldingsScrollViewer()
        {
            return BalanceHoldingsHorizontalScrollViewer;
        }

        private async Task VerifyBalanceAgainstMtsAsync()
        {
            BalanceStatusText.Text = "Hidden balance verification running...";
            AppendLog("balance verify started: kt00018 KRX/NXT + kt00005 KRX");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                KiwoomBalanceSnapshot kt00018Krx = await _tradingClient
                    .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketKrx, cts.Token)
                    .ConfigureAwait(true);
                KiwoomBalanceSnapshot kt00018Nxt = await _tradingClient
                    .GetEvaluationBalanceAsync(KiwoomTradingConstants.MarketNxt, cts.Token)
                    .ConfigureAwait(true);
                KiwoomBalanceSnapshot kt00005Krx = await _tradingClient
                    .GetExecutionBalanceAsync(KiwoomTradingConstants.MarketKrx, cts.Token)
                    .ConfigureAwait(true);

                var merged = kt00018Krx.Holdings
                    .Concat(kt00018Nxt.Holdings)
                    .GroupBy(x => x.StockCode, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        Code = group.Key,
                        Name = group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.StockName))?.StockName ?? string.Empty,
                        Quantity = group.Sum(x => x.HoldingQuantity),
                        Orderable = group.Sum(x => x.OrderableQuantity),
                        Evaluation = group.Sum(x => x.EvaluationAmount),
                        Profit = group.Sum(x => x.EvaluationProfit)
                    })
                    .OrderByDescending(x => Math.Abs(x.Evaluation))
                    .ToList();

                AppendLog($"balance verify kt00018 KRX: {kt00018Krx.Holdings.Count}items / eval {kt00018Krx.TotalEvaluationAmount:N0} / pl {kt00018Krx.TotalEvaluationProfit:N0} / rate {kt00018Krx.TotalProfitRate:N2}%");
                AppendLog($"balance verify kt00018 NXT: {kt00018Nxt.Holdings.Count}items / eval {kt00018Nxt.TotalEvaluationAmount:N0} / pl {kt00018Nxt.TotalEvaluationProfit:N0} / rate {kt00018Nxt.TotalProfitRate:N2}%");
                AppendLog($"balance verify kt00018 merged: {merged.Count}items / eval {merged.Sum(x => x.Evaluation):N0} / pl {merged.Sum(x => x.Profit):N0}");
                AppendLog($"balance verify kt00005 KRX: {kt00005Krx.Holdings.Count}items / eval {kt00005Krx.TotalEvaluationAmount:N0} / pl {kt00005Krx.TotalEvaluationProfit:N0} / rate {kt00005Krx.TotalProfitRate:N2}%");

                foreach (var row in merged.Take(8))
                {
                    AppendLog($"balance verify merged item: {row.Name}({row.Code}) qty {row.Quantity:N0} / able {row.Orderable:N0} / eval {row.Evaluation:N0} / pl {row.Profit:N0}");
                }

                BalanceStatusText.Text = "Hidden balance verification logged";
            }
            catch (Exception ex)
            {
                BalanceStatusText.Text = "Hidden balance verification failed";
                AppendLog($"balance verify error: {ex.GetType().Name} / {ex.Message}");
            }
        }
    }
}
