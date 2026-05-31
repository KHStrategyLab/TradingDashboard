using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard
{
    public partial class MainWindow
    {
        private async Task TrySendConditionEnterAlertAsync(WatchStockItem stock, string source, CancellationToken cancellationToken = default)
        {
            if (!ShouldSendConditionEnterAlert(stock))
                return;

            try
            {
                string message = BuildConditionEnterAlertMessage(stock, source);
                await _telegramNotifier.SendHtmlToDefaultAsync(message, cancellationToken).ConfigureAwait(false);
                Dispatcher.Invoke(() => AppendLog($"condition enter alert sent: {stock.Name} ({stock.Code}) / {source}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ReleaseConditionEnterAlertReservation(stock.Code);
                Dispatcher.Invoke(() => AppendLog($"condition enter alert error: {stock.Code} / {ex.Message}"));
            }
        }

        private bool ShouldSendConditionEnterAlert(WatchStockItem stock)
        {
            if (!_config.Telegram.Enabled ||
                string.IsNullOrWhiteSpace(_config.Telegram.BotToken) ||
                string.IsNullOrWhiteSpace(_config.Telegram.DefaultChatId))
                return false;

            if (stock == null || string.IsNullOrWhiteSpace(stock.Code))
                return false;

            DateTime now = DateTime.Now;
            lock (_conditionEnterAlertLock)
            {
                if (_conditionEnterAlertSentDate.Date != now.Date)
                {
                    _conditionEnterAlertSentStockCodes.Clear();
                    _conditionEnterAlertSentDate = now.Date;
                }

                return _conditionEnterAlertSentStockCodes.Add(stock.Code);
            }
        }

        private void ReleaseConditionEnterAlertReservation(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            lock (_conditionEnterAlertLock)
                _conditionEnterAlertSentStockCodes.Remove(code);
        }

        private static string BuildConditionEnterAlertMessage(WatchStockItem stock, string source)
        {
            string name = EscapeConditionEnterAlertHtml(string.IsNullOrWhiteSpace(stock.Name) ? stock.Code : stock.Name);
            string code = EscapeConditionEnterAlertHtml(stock.Code);
            string safeSource = EscapeConditionEnterAlertHtml(source);
            string badges = EscapeConditionEnterAlertHtml(stock.AlertListBadgeText);
            string rate = EscapeConditionEnterAlertHtml(stock.ChangeRateText);
            string price = stock.CurrentPrice > 0 ? $"{stock.CurrentPrice:N0}" : "-";

            return string.Join(Environment.NewLine, new[]
            {
                $"<b>조건식 편입</b> {name} ({code})",
                $"source: {safeSource}",
                $"price: {price} / rate: {rate}",
                string.IsNullOrWhiteSpace(badges) ? "badge: -" : $"badge: {badges}"
            });
        }

        private static string EscapeConditionEnterAlertHtml(string value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}
