using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public class TelegramNotifier
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly TelegramSettings _settings;

        public TelegramNotifier(TelegramSettings settings)
        {
            _settings = settings ?? new TelegramSettings();
        }

        public async Task SendToDefaultAsync(string message, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(_settings.DefaultChatId))
                return;

            await SendAsync(_settings.DefaultChatId, message, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendToAllAsync(string message, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!_settings.Enabled)
                return;

            IEnumerable<string> chatIds = _settings.ChatIds ?? Enumerable.Empty<string>();

            foreach (string chatId in chatIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                await SendAsync(chatId, message, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SendAsync(string chatId, string message, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!_settings.Enabled)
                return;

            if (string.IsNullOrWhiteSpace(_settings.BotToken) || string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(message))
                return;

            string url = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";

            using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["text"] = message,
                ["disable_web_page_preview"] = "true"
            }))
            {
                using (HttpResponseMessage response = await HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }
            }
        }
    }
}
