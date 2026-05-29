using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private async Task TrySendLateNewsAlertAsync(WatchStockItem stock, CancellationToken cancellationToken = default)
        {
            if (!ShouldSendLateNewsAlert(stock))
                return;

            try
            {
                int count = Math.Clamp(_config.LateNewsAlert.NewsCount, 1, 10);
                List<NewsItem> newsItems = await _newsService.GetLatestNewsAsync(stock.Name, count, cancellationToken).ConfigureAwait(false);
                newsItems = [.. newsItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Link))
                    .Take(count)];

                if (newsItems.Count == 0)
                {
                    Dispatcher.Invoke(() => AppendLog($"Late News skipped(no news): {stock.Name} ({stock.Code})"));
                    return;
                }

                string message = BuildLateNewsMessage(stock.Name, newsItems);
                await _telegramNotifier.SendHtmlToDefaultAsync(message, cancellationToken).ConfigureAwait(false);
                Dispatcher.Invoke(() => AppendLog($"Late News sent: {stock.Name} ({stock.Code})"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ReleaseLateNewsReservation(stock.Code);
                Dispatcher.Invoke(() => AppendLog($"Late News error: {stock.Code} / {ex.Message}"));
            }
        }

        private bool ShouldSendLateNewsAlert(WatchStockItem stock)
        {
            if (!_config.LateNewsAlert.Enabled)
                return false;

            if (!_config.Telegram.Enabled ||
                string.IsNullOrWhiteSpace(_config.Telegram.BotToken) ||
                string.IsNullOrWhiteSpace(_config.Telegram.DefaultChatId))
                return false;

            if (stock == null || string.IsNullOrWhiteSpace(stock.Code) || string.IsNullOrWhiteSpace(stock.Name))
                return false;

            DateTime now = DateTime.Now;
            if ((now - _lateNewsAppStartedAt).TotalMinutes < Math.Max(0, _config.LateNewsAlert.WarmupMinutes))
                return false;

            if (!IsWithinLateNewsWindow(now.TimeOfDay))
                return false;

            lock (_lateNewsLock)
            {
                if (_lateNewsSentDate.Date != now.Date)
                {
                    _lateNewsSentStockCodes.Clear();
                    _lateNewsSentDate = now.Date;
                }

                return _lateNewsSentStockCodes.Add(stock.Code);
            }
        }

        private void ReleaseLateNewsReservation(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            lock (_lateNewsLock)
            {
                _lateNewsSentStockCodes.Remove(code);
            }
        }

        private bool IsWithinLateNewsWindow(TimeSpan now)
        {
            TimeSpan start = ParseLateNewsTime(_config.LateNewsAlert.StartTime, new TimeSpan(8, 0, 0));
            TimeSpan end = ParseLateNewsTime(_config.LateNewsAlert.EndTime, new TimeSpan(11, 0, 0));

            return start <= end
                ? now >= start && now <= end
                : now >= start || now <= end;
        }

        private static TimeSpan ParseLateNewsTime(string value, TimeSpan fallback)
        {
            return TimeSpan.TryParse(value, out TimeSpan parsed)
                ? parsed
                : fallback;
        }

        private string BuildLateNewsMessage(string stockName, IReadOnlyList<NewsItem> newsItems)
        {
            int titleMaxLength = Math.Clamp(_config.LateNewsAlert.TitleMaxLength, 5, 80);
            string safeStockName = EscapeTelegramHtml(stockName);
            var lines = new List<string>
            {
                $"{safeStockName} - 난가!!!??",
                string.Empty
            };

            foreach (NewsItem item in newsItems)
            {
                string title = EscapeTelegramHtml(ShortenTelegramText(item.Title, titleMaxLength));
                string link = EscapeTelegramHtml(item.Link);
                lines.Add($"{safeStockName} / {title}");
                lines.Add($"<a href=\"{link}\">기사 보기</a>");
                lines.Add(string.Empty);
            }

            return string.Join(Environment.NewLine, lines).TrimEnd();
        }

        private static string ShortenTelegramText(string text, int maxLength)
        {
            string clean = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : WebUtility.HtmlDecode(text).Trim();

            if (clean.Length <= maxLength)
                return clean;

            return clean[..maxLength] + "…";
        }

        private static string EscapeTelegramHtml(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
