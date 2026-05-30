using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;
using TradingDashboard.Services;

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
                const int candidateCount = 20;
                List<NewsItem> newsItems = await _newsService.GetLatestNewsAsync(stock.Name, candidateCount, cancellationToken).ConfigureAwait(false);
                newsItems = [.. newsItems.Where(item => !string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Link))];
                int beforeFilterCount = newsItems.Count;
                IReadOnlyList<NewsKeywordFilterService.RankedNewsItem> rankedNews = _newsKeywordFilterService.Rank(newsItems);
                newsItems = [.. rankedNews.Take(count).Select(item => item.Item)];

                if (newsItems.Count == 0)
                {
                    Dispatcher.Invoke(() => AppendLog(
                        beforeFilterCount == 0
                            ? $"Late News skipped(no news): {stock.Name} ({stock.Code})"
                            : $"Late News skipped(filtered): {stock.Name} ({stock.Code}) / {beforeFilterCount}->0"));
                    return;
                }

                await SendLateNewsBundleMessageAsync(stock.Name, newsItems, cancellationToken).ConfigureAwait(false);
                string topReason = FormatLateNewsFilterReason(rankedNews.FirstOrDefault());
                Dispatcher.Invoke(() => AppendLog($"Late News sent: {stock.Name} ({stock.Code}) / filter {beforeFilterCount}->{newsItems.Count}{topReason}"));
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

        private async Task SendLateNewsBundleMessageAsync(string stockName, IReadOnlyList<NewsItem> newsItems, CancellationToken cancellationToken)
        {
            string message = BuildLateNewsBundleMessage(stockName, newsItems);
            await _telegramNotifier.SendHtmlToDefaultAsync(message, disableWebPagePreview: false, cancellationToken).ConfigureAwait(false);
        }

        private static string BuildLateNewsBundleMessage(string stockName, IReadOnlyList<NewsItem> newsItems)
        {
            string safeStockName = EscapeTelegramHtml(stockName);
            var lines = new List<string>
            {
                $"-{safeStockName}-"
            };

            bool previewLinkAdded = false;
            int index = 1;
            foreach (NewsItem item in newsItems)
            {
                if (string.IsNullOrWhiteSpace(item.Link))
                    continue;

                string safeTitle = EscapeTelegramHtml(ShortenTelegramText(item.Title));
                string safeLink = EscapeTelegramHtml(item.Link);
                if (!previewLinkAdded)
                {
                    lines.Add($"<a href=\"{safeLink}\">&#8203;</a>");
                    previewLinkAdded = true;
                }

                if (!string.IsNullOrWhiteSpace(safeTitle))
                    lines.Add(safeTitle);

                lines.Add($"<a href=\"{safeLink}\">기사보기 {index}</a>");
                index++;
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatLateNewsFilterReason(NewsKeywordFilterService.RankedNewsItem? top)
        {
            if (top == null)
                return string.Empty;

            string words = string.Join(",", top.Words.Take(4));
            return string.IsNullOrWhiteSpace(words)
                ? $" / score {top.Score}"
                : $" / score {top.Score} / {words}";
        }

        private static string ShortenTelegramText(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : WebUtility.HtmlDecode(text).Trim();
        }

        private static string EscapeTelegramHtml(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
