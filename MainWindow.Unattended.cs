using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private static readonly TimeSpan DailyStartupRefreshTime = new(7, 30, 0);
        private static readonly TimeSpan RealtimeWatchdogInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RealtimeSilenceReconnectAfter = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan RealtimeReconnectDelay = TimeSpan.FromSeconds(3);

        private readonly SemaphoreSlim _realtimeConnectionLock = new(1, 1);
        private CancellationTokenSource? _unattendedOpsCts;
        private DateTime _dailyStartupRefreshDate = DateTime.MinValue;
        private DateTime _lastRealtimeMessageAt = DateTime.MinValue;
        private DateTime _lastRealtimeConnectAt = DateTime.MinValue;
        private DateTime _lastRealtimeWatchdogLogAt = DateTime.MinValue;
        private bool _isDailyStartupRefreshRunning;

        private void StartUnattendedOperations()
        {
            StopUnattendedOperations();
            _unattendedOpsCts = new CancellationTokenSource();
            CancellationToken token = _unattendedOpsCts.Token;
            _ = Task.Run(() => RunRealtimeWatchdogAsync(token), token);
            _ = Task.Run(() => RunDailyStartupRefreshSchedulerAsync(token), token);
            AppendLog("unattended monitor started: WS watchdog / daily 07:30 refresh");
        }

        private void StopUnattendedOperations()
        {
            try
            {
                _unattendedOpsCts?.Cancel();
                _unattendedOpsCts?.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _unattendedOpsCts = null;
            }
        }

        private void MarkDailyStartupRefreshCompletedByStartup()
        {
            if (DateTime.Now.TimeOfDay >= DailyStartupRefreshTime)
                _dailyStartupRefreshDate = DateTime.Today;
        }

        private async Task RunDailyStartupRefreshSchedulerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    DateTime todayTarget = now.Date.Add(DailyStartupRefreshTime);
                    DateTime nextTarget = now < todayTarget ? todayTarget : todayTarget.AddDays(1);
                    TimeSpan delay = nextTarget - now;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                    await InvokeOnUiThreadAsync(
                        () => RunDailyStartupRefreshAsync("07:30 scheduled"),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => AppendLog($"daily 07:30 refresh scheduler error: {ex.Message}"));
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task RunDailyStartupRefreshAsync(string reason)
        {
            DateTime today = DateTime.Today;
            if (_dailyStartupRefreshDate == today || _isDailyStartupRefreshRunning)
                return;

            _isDailyStartupRefreshRunning = true;
            try
            {
                AppendLog($"daily startup refresh started: {reason}");
                SetStartupLoading(
                    true,
                    "Daily 07:30 refresh...",
                    "Balance, condition, realtime feeds will be refreshed",
                    "This runs once per trading day");

                await PrimeMarketStatusBeforeWatchlistAsync();
                await RefreshBalanceAsync("daily 07:30 refresh");
                LoadWatchlistCache();
                await LoadWatchListFromKiwoomConditionAsync();
                _ = LoadMarketNewsAfterStartupDelayAsync();

                _dailyStartupRefreshDate = today;
                AppendLog("daily startup refresh completed: 07:30 ready");
            }
            catch (Exception ex)
            {
                AppendLog($"daily startup refresh error: {ex.Message}");
            }
            finally
            {
                SetStartupLoading(false, string.Empty, string.Empty, string.Empty);
                _isDailyStartupRefreshRunning = false;
            }
        }

        private async Task RunRealtimeWatchdogAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(RealtimeWatchdogInterval, cancellationToken).ConfigureAwait(false);
                    if (!_config.Kiwoom.UseRestApi || _watchStockByCode.Count == 0)
                        continue;

                    string reason = ResolveRealtimeWatchdogReconnectReason();
                    if (string.IsNullOrWhiteSpace(reason))
                        continue;

                    await InvokeOnUiThreadAsync(
                        () => RestartRealtimeTradeAsync($"watchdog: {reason}"),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => AppendLog($"0B watchdog error: {ex.Message}"));
                }
            }
        }

        private string ResolveRealtimeWatchdogReconnectReason()
        {
            ClientWebSocket? ws = _realtimeWs;
            if (ws == null)
                return "socket missing";

            if (ws.State != WebSocketState.Open)
                return $"socket {ws.State}";

            DateTime lastMessageAt = _lastRealtimeMessageAt == DateTime.MinValue
                ? _lastRealtimeConnectAt
                : _lastRealtimeMessageAt;
            if (lastMessageAt == DateTime.MinValue)
                return string.Empty;

            TimeSpan silentFor = DateTime.Now - lastMessageAt;
            if (silentFor >= RealtimeSilenceReconnectAfter)
                return $"silent {silentFor.TotalSeconds:N0}s";

            return string.Empty;
        }

        private async Task RestartRealtimeTradeAsync(string reason)
        {
            DateTime now = DateTime.Now;
            if ((now - _lastRealtimeWatchdogLogAt).TotalSeconds >= 10)
            {
                _lastRealtimeWatchdogLogAt = now;
                AppendLog($"0B reconnect requested: {reason}");
            }

            await StartRealtimeTradeAsync();
        }

        private async Task RestartRealtimeTradeAfterDelayAsync(string reason)
        {
            CancellationToken token = _unattendedOpsCts?.Token ?? CancellationToken.None;
            try
            {
                await Task.Delay(RealtimeReconnectDelay, token).ConfigureAwait(false);
                await InvokeOnUiThreadAsync(
                    () => RestartRealtimeTradeAsync(reason),
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        private async Task InvokeOnUiThreadAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            if (Dispatcher.CheckAccess())
            {
                await action().ConfigureAwait(true);
                return;
            }

            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    completion.SetResult(null);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });

            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
