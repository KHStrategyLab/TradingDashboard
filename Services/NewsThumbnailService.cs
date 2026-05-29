using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TradingDashboard.Services
{
    public sealed class NewsThumbnailService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly Regex OgImageRegex = new(
            "<meta\\s+(?:[^>]*?(?:property|name)=[\"']og:image[\"'][^>]*?content=[\"'](?<url>[^\"']+)[\"']|[^>]*?content=[\"'](?<url>[^\"']+)[\"'][^>]*?(?:property|name)=[\"']og:image[\"'])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<string> GetThumbnailUrlAsync(string articleUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(articleUrl))
                return string.Empty;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, articleUrl);
                using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Match match = OgImageRegex.Match(html);
                if (!match.Success)
                    return string.Empty;

                string url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                    return url;
            }
            catch
            {
                // Some news sites block crawlers or redirect oddly. Thumbnails are optional.
            }

            return string.Empty;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TradingDashboard/1.0");
            return client;
        }
    }
}
