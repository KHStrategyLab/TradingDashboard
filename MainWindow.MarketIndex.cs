using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;
using TradingDashboard.Services;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private static readonly TimeSpan MarketIndexRefreshInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan MarketIndexRefreshStart = new(8, 0, 0);
        private static readonly TimeSpan MarketIndexRefreshEnd = new(18, 0, 0);

        private bool _isMarketIndexRefreshRunning;
        private DateTime _lastMarketIndexRefreshAt = DateTime.MinValue;
        private bool _marketIndexAfterHoursPauseLogged;
        private string _lastMarketIndexTickerText = string.Empty;

        private async Task RunMarketIndexRefreshSchedulerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(MarketIndexRefreshInterval, cancellationToken).ConfigureAwait(false);
                    await InvokeOnUiThreadAsync(
                        () => RefreshMarketIndexTickerAsync("timer", force: false),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => AppendLog($"market index refresh scheduler error: {ex.Message}"));
                }
            }
        }

        private async Task RefreshMarketIndexTickerAsync(string reason, bool force)
        {
            if (!_config.Kiwoom.UseRestApi)
                return;

            if (_isMarketIndexRefreshRunning)
                return;

            DateTime now = DateTime.Now;
            if (!force)
            {
                TimeSpan tod = now.TimeOfDay;
                if (tod < MarketIndexRefreshStart || tod >= MarketIndexRefreshEnd)
                {
                    if (!_marketIndexAfterHoursPauseLogged)
                    {
                        _marketIndexAfterHoursPauseLogged = true;
                        AppendLog("market index refresh paused after 18:00 or before 08:00");
                    }

                    return;
                }

                _marketIndexAfterHoursPauseLogged = false;

                if (now - _lastMarketIndexRefreshAt < MarketIndexRefreshInterval)
                    return;
            }

            _isMarketIndexRefreshRunning = true;
            _lastMarketIndexRefreshAt = now;
            try
            {
                AppendLog($"market index refresh started: {reason}");
                IReadOnlyList<MarketIndexSnapshot> snapshots = await _kiwoomConditionService.GetMarketIndexSnapshotsAsync();
                if (snapshots.Count == 0 || snapshots.All(IsEmptyMarketIndexSnapshot))
                {
                    AppendLog("market index query completed but empty");
                    return;
                }

                string ticker = FormatMarketIndexTicker(snapshots);
                ApplyMarketIndexTickerInlines(snapshots);

                if (!string.Equals(_lastMarketIndexTickerText, ticker, StringComparison.Ordinal))
                    _lastMarketIndexTickerText = ticker;

                AppendLog($"market index applied: {ticker}");
            }
            catch (Exception ex)
            {
                AppendLog($"market index refresh error: {ex.Message}");
            }
            finally
            {
                _isMarketIndexRefreshRunning = false;
            }
        }

        private void ApplyMarketIndexTickerInlines(IReadOnlyList<MarketIndexSnapshot> snapshots)
        {
            MarketTickerText.Inlines.Clear();
            bool first = true;
            Brush separatorBrush = (Brush)FindResource("TextSubBrush");
            foreach (MarketIndexSnapshot snapshot in snapshots)
            {
                if (!first)
                    MarketTickerText.Inlines.Add(new Run("   ") { Foreground = separatorBrush });

                MarketTickerText.Inlines.Add(new Run(FormatMarketIndexSnapshot(snapshot))
                {
                    Foreground = ResolveMarketIndexSnapshotBrush(snapshot)
                });
                first = false;
            }
        }

        private Brush ResolveMarketIndexSnapshotBrush(MarketIndexSnapshot snapshot)
        {
            decimal direction = snapshot.DayChange ?? snapshot.ChangeRate ?? 0m;
            if (direction > 0)
                return _upColorBrush;
            if (direction < 0)
                return _downColorBrush;
            return _whiteBrush;
        }

        private static bool IsEmptyMarketIndexSnapshot(MarketIndexSnapshot snapshot)
        {
            return snapshot.Current is null &&
                   snapshot.DayChange is null &&
                   snapshot.ChangeRate is null;
        }

        private static string FormatMarketIndexTicker(IEnumerable<MarketIndexSnapshot> snapshots)
        {
            return string.Join("   ", snapshots.Select(FormatMarketIndexSnapshot));
        }

        private static string FormatMarketIndexSnapshot(MarketIndexSnapshot snapshot)
        {
            string current = snapshot.Current is decimal price
                ? price.ToString("#,0.##", CultureInfo.InvariantCulture)
                : "-";
            decimal? directionValue = snapshot.DayChange ?? snapshot.ChangeRate;
            string arrow = "·";
            if (directionValue is decimal direction)
                arrow = direction > 0 ? "▲" : direction < 0 ? "▼" : "·";

            string diff = snapshot.DayChange is decimal dayChange
                ? FormatSignedDecimal(dayChange, "#,0.##")
                : "-";
            string rate = snapshot.ChangeRate is decimal changeRate
                ? $"{FormatSignedDecimal(changeRate, "0.##")}%"
                : "-";

            return $"{snapshot.Name} {current} {arrow} {diff} ({rate})";
        }

        private static string FormatSignedDecimal(decimal value, string format)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return sign + value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
