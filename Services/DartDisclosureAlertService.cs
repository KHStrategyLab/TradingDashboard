using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class DartDisclosureAlertService(DartDisclosureService disclosureService, TelegramNotifier telegramNotifier)
    {
        private static readonly string[] PriorityDisclosureKeywords =
        [
            "수주",
            "공급계약",
            "계약체결",
            "단일판매",
            "판매ㆍ공급계약",
            "판매·공급계약",
            "신규시설투자",
            "시설투자",
            "증설",
            "자기주식취득",
            "자기주식 취득",
            "자사주취득",
            "자사주 취득",
            "자기주식소각",
            "자기주식 소각",
            "자사주소각",
            "자사주 소각",
            "현금ㆍ현물배당",
            "현금·현물배당",
            "배당",
            "주주환원",
            "기업가치제고",
            "기업가치 제고",
            "밸류업",
            "지분취득",
            "지분 취득",
            "타법인주식",
            "타법인 주식",
            "출자증권 취득",
            "합병",
            "인수"
        ];

        private readonly DartDisclosureService _disclosureService = disclosureService;
        private readonly TelegramNotifier _telegramNotifier = telegramNotifier;
        private readonly SemaphoreSlim _alertGate = new(1, 1);
        private readonly HashSet<string> _sentReceiptNos = new(StringComparer.Ordinal);
        private DateTime _sentReceiptDate = DateTime.Today;

        public event Action<string>? AlertLog;

        public async Task TrySendRecentDisclosureAlertAsync(WatchStockItem stock, CancellationToken cancellationToken = default)
        {
            if (stock == null || string.IsNullOrWhiteSpace(stock.Code) || string.IsNullOrWhiteSpace(stock.Name))
                return;

            await _alertGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ResetSentCacheIfNeeded();

                List<DisclosureItem> disclosures = await _disclosureService
                    .GetRecentDisclosuresAsync(stock.Code, lookbackDays: 1, count: 5, cancellationToken)
                    .ConfigureAwait(false);

                List<DisclosureItem> freshDisclosures = [.. disclosures
                    .Where(IsTodayOrYesterday)
                    .Where(IsPriorityDisclosure)
                    .Where(item => !string.IsNullOrWhiteSpace(item.ReceiptNo))
                    .Where(item => _sentReceiptNos.Add(item.ReceiptNo))
                    .Take(3)];

                if (freshDisclosures.Count == 0)
                {
                    AlertLog?.Invoke($"DART alert skipped(no fresh filing): {stock.Name} ({stock.Code})");
                    return;
                }

                string message = BuildDisclosureAlertMessage(stock.Name, freshDisclosures);
                await _telegramNotifier.SendHtmlToDefaultAsync(message, cancellationToken).ConfigureAwait(false);
                AlertLog?.Invoke($"DART material alert sent: {stock.Name} ({stock.Code}) / {freshDisclosures.Count}items");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AlertLog?.Invoke($"DART alert error: {stock.Code} / {ex.Message}");
            }
            finally
            {
                _alertGate.Release();
            }
        }

        private void ResetSentCacheIfNeeded()
        {
            DateTime today = DateTime.Today;
            if (_sentReceiptDate == today)
                return;

            _sentReceiptNos.Clear();
            _sentReceiptDate = today;
        }

        private static bool IsTodayOrYesterday(DisclosureItem item)
        {
            if (!DateTime.TryParseExact(item.ReceiptDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime receiptDate))
                return false;

            DateTime today = DateTime.Today;
            return receiptDate.Date == today || receiptDate.Date == today.AddDays(-1);
        }

        private static bool IsPriorityDisclosure(DisclosureItem item)
        {
            string text = $"{item.Title} {item.ReportName}";
            return PriorityDisclosureKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildDisclosureAlertMessage(string stockName, IReadOnlyList<DisclosureItem> disclosures)
        {
            string safeStockName = EscapeHtml(stockName);
            var lines = new List<string>
            {
                $"-{safeStockName} 공시-"
            };

            int index = 1;
            foreach (DisclosureItem disclosure in disclosures)
            {
                string title = EscapeHtml(disclosure.Title);
                string link = EscapeHtml(disclosure.Link);
                string date = EscapeHtml(disclosure.DateText);
                lines.Add($"{index}. {date} {title}");
                if (!string.IsNullOrWhiteSpace(link))
                    lines.Add($"<a href=\"{link}\">공시보기 {index}</a>");
                index++;
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string EscapeHtml(string value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}
