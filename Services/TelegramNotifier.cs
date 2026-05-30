using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public class TelegramNotifier(TelegramSettings settings)
    {
        private static readonly HttpClient HttpClient = new();
        private static readonly SemaphoreSlim TelegramSendGate = new(1, 1);
        private static readonly TimeSpan TelegramSendInterval = TimeSpan.FromMilliseconds(1100);
        private static readonly TimeSpan TelegramDefault429Cooldown = TimeSpan.FromSeconds(2);
        private static DateTime _lastTelegramSendUtc = DateTime.MinValue;
        private static DateTime _telegramBlockedUntilUtc = DateTime.MinValue;
        private readonly TelegramSettings _settings = settings ?? new TelegramSettings();

        public event Action<string>? ApiLimitLog;

        public async Task SendToDefaultAsync(string message, CancellationToken cancellationToken = default)
        {
            await SendToDefaultAsync(message, disableWebPagePreview: true, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendToDefaultAsync(string message, bool disableWebPagePreview, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DefaultChatId))
                return;

            await SendAsync(_settings.DefaultChatId, message, parseMode: string.Empty, ignoreEnabled: false, disableWebPagePreview, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendManualToDefaultAsync(string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DefaultChatId))
                return;

            await SendAsync(_settings.DefaultChatId, message, parseMode: string.Empty, ignoreEnabled: true, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendHtmlToDefaultAsync(string message, CancellationToken cancellationToken = default)
        {
            await SendHtmlToDefaultAsync(message, disableWebPagePreview: true, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendHtmlToDefaultAsync(string message, bool disableWebPagePreview, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.DefaultChatId))
                return;

            await SendAsync(_settings.DefaultChatId, message, parseMode: "HTML", ignoreEnabled: false, disableWebPagePreview, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendToAllAsync(string message, CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
                return;

            IEnumerable<string> chatIds = _settings.ChatIds ?? Enumerable.Empty<string>();

            foreach (string chatId in chatIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                await SendAsync(chatId, message, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SendAsync(string chatId, string message, CancellationToken cancellationToken = default)
        {
            await SendAsync(chatId, message, parseMode: string.Empty, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendAsync(string chatId, string message, string parseMode, CancellationToken cancellationToken = default)
        {
            await SendAsync(chatId, message, parseMode, ignoreEnabled: false, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendAsync(string chatId, string message, string parseMode, bool ignoreEnabled, CancellationToken cancellationToken = default)
        {
            await SendAsync(chatId, message, parseMode, ignoreEnabled, disableWebPagePreview: true, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendAsync(string chatId, string message, string parseMode, bool ignoreEnabled, bool disableWebPagePreview, CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled && !ignoreEnabled)
                return;

            if (string.IsNullOrWhiteSpace(_settings.BotToken) || string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(message))
                return;

            var body = new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["text"] = message,
                ["disable_web_page_preview"] = disableWebPagePreview ? "true" : "false"
            };

            if (!string.IsNullOrWhiteSpace(parseMode))
                body["parse_mode"] = parseMode;

            await SendTelegramRequestAsync(chatId, body, cancellationToken).ConfigureAwait(false);
        }

        private async Task SendTelegramRequestAsync(string chatId, Dictionary<string, string> body, CancellationToken cancellationToken)
        {
            string url = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";

            await TelegramSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WaitTelegramSlotAsync(cancellationToken).ConfigureAwait(false);
                using HttpResponseMessage response = await PostTelegramFormAsync(url, body, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != (HttpStatusCode)429)
                {
                    response.EnsureSuccessStatusCode();
                    return;
                }

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                TimeSpan retryDelay = GetTelegramRetryDelay(response, responseBody);
                ApiLimitLog?.Invoke($"Telegram 429: chat {chatId} / wait {retryDelay.TotalSeconds:0.##}s");
                _telegramBlockedUntilUtc = DateTime.UtcNow + retryDelay;

                await WaitTelegramSlotAsync(cancellationToken).ConfigureAwait(false);
                using HttpResponseMessage retryResponse = await PostTelegramFormAsync(url, body, cancellationToken).ConfigureAwait(false);
                if (retryResponse.StatusCode == (HttpStatusCode)429)
                    ApiLimitLog?.Invoke($"Telegram 429 retry failed: chat {chatId}");

                retryResponse.EnsureSuccessStatusCode();
            }
            finally
            {
                TelegramSendGate.Release();
            }
        }

        private static async Task<HttpResponseMessage> PostTelegramFormAsync(string url, Dictionary<string, string> body, CancellationToken cancellationToken)
        {
            using var content = new FormUrlEncodedContent(body);
            return await HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }

        private static async Task WaitTelegramSlotAsync(CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _telegramBlockedUntilUtc)
                await Task.Delay(_telegramBlockedUntilUtc - now, cancellationToken).ConfigureAwait(false);

            now = DateTime.UtcNow;
            if (_lastTelegramSendUtc != DateTime.MinValue && now - _lastTelegramSendUtc < TelegramSendInterval)
                await Task.Delay(TelegramSendInterval - (now - _lastTelegramSendUtc), cancellationToken).ConfigureAwait(false);

            _lastTelegramSendUtc = DateTime.UtcNow;
        }

        private static TimeSpan GetTelegramRetryDelay(HttpResponseMessage response, string responseBody)
        {
            TimeSpan? headerDelay = response.Headers.RetryAfter?.Delta;
            if (headerDelay.HasValue && headerDelay.Value > TimeSpan.Zero)
                return headerDelay.Value;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("parameters", out JsonElement parameters) &&
                    parameters.TryGetProperty("retry_after", out JsonElement retryAfter) &&
                    retryAfter.TryGetInt32(out int seconds) &&
                    seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            catch
            {
                // Telegram may return plain text on some failures.
            }

            return TelegramDefault429Cooldown;
        }
    }
}
