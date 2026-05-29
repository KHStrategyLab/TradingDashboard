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
    public class NaverNewsService(NaverNewsSettings settings)
    {
        private static readonly HttpClient HttpClient = new();

        private readonly NaverNewsSettings _settings = settings ?? new NaverNewsSettings();

        public async Task<List<NewsItem>> GetLatestNewsAsync(string stockName, int? count = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(stockName))
                return [];

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
                return [];

            int displayCount = count ?? _settings.DisplayCount;
            if (displayCount <= 0)
                displayCount = 5;

            if (displayCount > 100)
                displayCount = 100;

            string sort = string.IsNullOrWhiteSpace(_settings.Sort) ? "date" : _settings.Sort;
            string query = Uri.EscapeDataString(stockName);
            string url = $"https://openapi.naver.com/v1/search/news.json?query={query}&display={displayCount}&start=1&sort={sort}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Naver-Client-Id", _settings.ClientId);
            request.Headers.Add("X-Naver-Client-Secret", _settings.ClientSecret);

            using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            NaverNewsResponse? result = JsonSerializer.Deserialize<NaverNewsResponse>(json, options);

            if (result?.Items == null)
                return [];

            return [.. result.Items
                .Take(displayCount)
                .Select(item =>
                {
                    string link = string.IsNullOrWhiteSpace(item.OriginalLink) ? item.Link : item.OriginalLink;
                    return new NewsItem
                    {
                        Title = CleanText(item.Title),
                        Link = link,
                        Description = CleanText(item.Description),
                        PubDate = FormatPubDate(item.PubDate),
                        Source = ExtractHost(link),
                        ThumbnailText = "NEWS"
                    };
                })];
        }

        public Task<List<NewsItem>> GetMarketNewsAsync(CancellationToken cancellationToken = default)
        {
            string query = string.IsNullOrWhiteSpace(_settings.MarketQuery)
                ? "증권 | 증시 | 코스피 | 코스닥 | 주식 | 금융"
                : _settings.MarketQuery;
            int count = _settings.MarketDisplayCount > 0 ? _settings.MarketDisplayCount : 20;
            return GetLatestNewsAsync(query, count, cancellationToken);
        }

        public Task<List<NewsItem>> SearchNewsAsync(string query, CancellationToken cancellationToken = default)
        {
            int count = _settings.MarketDisplayCount > 0 ? _settings.MarketDisplayCount : 20;
            return GetLatestNewsAsync(query, count, cancellationToken);
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

            return DateTimeOffset.TryParse(pubDate, out DateTimeOffset parsed)
                ? parsed.LocalDateTime.ToString("MM-dd HH:mm")
                : pubDate;
        }

        private static string ExtractHost(string link)
        {
            return Uri.TryCreate(link, UriKind.Absolute, out Uri? uri)
                ? uri.Host.Replace("www.", string.Empty)
                : "news";
        }

        private class NaverNewsResponse
        {
            public List<NaverNewsRawItem> Items { get; set; } = [];
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
