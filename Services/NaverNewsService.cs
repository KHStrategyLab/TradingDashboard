using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public class NaverNewsService
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        private readonly NaverNewsSettings _settings;

        public NaverNewsService(NaverNewsSettings settings)
        {
            _settings = settings ?? new NaverNewsSettings();
        }

        public async Task<List<NewsItem>> GetLatestNewsAsync(string stockName, int? count = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(stockName))
                return new List<NewsItem>();

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
                return new List<NewsItem>();

            int displayCount = count ?? _settings.DisplayCount;
            if (displayCount <= 0)
                displayCount = 5;

            if (displayCount > 100)
                displayCount = 100;

            string sort = string.IsNullOrWhiteSpace(_settings.Sort) ? "date" : _settings.Sort;
            string query = Uri.EscapeDataString(stockName);
            string url = $"https://openapi.naver.com/v1/search/news.json?query={query}&display={displayCount}&start=1&sort={sort}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("X-Naver-Client-Id", _settings.ClientId);
                request.Headers.Add("X-Naver-Client-Secret", _settings.ClientSecret);

                using (HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    NaverNewsResponse result = JsonSerializer.Deserialize<NaverNewsResponse>(json, options);

                    if (result == null || result.Items == null)
                        return new List<NewsItem>();

                    return result.Items
                        .Take(displayCount)
                        .Select(item => new NewsItem
                        {
                            Title = CleanText(item.Title),
                            Link = string.IsNullOrWhiteSpace(item.OriginalLink) ? item.Link : item.OriginalLink,
                            PubDate = FormatPubDate(item.PubDate),
                            Source = ExtractHost(string.IsNullOrWhiteSpace(item.OriginalLink) ? item.Link : item.OriginalLink)
                        })
                        .ToList();
                }
            }
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string noTags = Regex.Replace(text, "<.*?>", string.Empty);
            return WebUtility.HtmlDecode(noTags).Trim();
        }

        private static string FormatPubDate(string pubDate)
        {
            if (string.IsNullOrWhiteSpace(pubDate))
                return string.Empty;

            if (DateTimeOffset.TryParse(pubDate, out DateTimeOffset parsed))
                return parsed.LocalDateTime.ToString("MM-dd HH:mm");

            return pubDate;
        }

        private static string ExtractHost(string link)
        {
            if (Uri.TryCreate(link, UriKind.Absolute, out Uri uri))
                return uri.Host.Replace("www.", string.Empty);

            return "뉴스";
        }

        private class NaverNewsResponse
        {
            public List<NaverNewsRawItem> Items { get; set; } = new List<NaverNewsRawItem>();
        }

        private class NaverNewsRawItem
        {
            public string Title { get; set; } = string.Empty;
            public string OriginalLink { get; set; } = string.Empty;
            public string Link { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string PubDate { get; set; } = string.Empty;
        }
    }
}
