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

        private const string DefaultMarketQuery =
            "\uC99D\uC2DC|\uCF54\uC2A4\uD53C|\uCF54\uC2A4\uB2E5|\uD2B9\uC9D5\uC8FC|\uAE09\uB4F1|\uACF5\uC2DC|\uC218\uC8FC|\uACF5\uAE09\uACC4\uC57D|\uBC18\uB3C4\uCCB4|AI|2\uCC28\uC804\uC9C0|\uB85C\uBD07";

        private static readonly string[] MarketExcludeWords =
        [
            "\uCD95\uAD6C", "\uC57C\uAD6C", "\uB18D\uAD6C", "\uBC30\uAD6C", "\uACE8\uD504", "\uC2A4\uD3EC\uCE20",
            "\uC120\uC218", "\uAC10\uB3C5", "\uB9AC\uADF8", "\uC6D4\uB4DC\uCEF5", "\uC62C\uB9BC\uD53D",
            "\uC5F0\uC608", "\uBC30\uC6B0", "\uAC00\uC218", "\uB4DC\uB77C\uB9C8", "\uC601\uD654"
        ];

        private static readonly (string Word, int Score)[] MarketScoreWords =
        [
            ("\uD2B9\uC9D5\uC8FC", 8),
            ("\uC0C1\uD55C\uAC00", 8),
            ("\uAE09\uB4F1", 7),
            ("\uACF5\uAE09\uACC4\uC57D", 7),
            ("\uB300\uADDC\uBAA8 \uC218\uC8FC", 7),
            ("\uC218\uC8FC", 6),
            ("\uACF5\uC2DC", 5),
            ("\uD751\uC790\uC804\uD658", 5),
            ("\uD134\uC5B4\uB77C\uC6B4\uB4DC", 5),
            ("\uCD5C\uB300\uC2E4\uC801", 5),
            ("\uD6C8\uD48D", 4),
            ("\uAE30\uB300\uAC10", 4),
            ("\uC138\uACC4 \uCD5C\uCD08", 4),
            ("\uB3C5\uC810", 4),
            ("\uC591\uC0B0", 4),
            ("\uC2E4\uC801", 3),
            ("\uACC4\uC57D", 3),
            ("\uBC18\uB3C4\uCCB4", 3),
            ("AI", 3),
            ("2\uCC28\uC804\uC9C0", 3),
            ("\uB85C\uBD07", 3),
            ("\uC99D\uC2DC", 2),
            ("\uCF54\uC2A4\uD53C", 2),
            ("\uCF54\uC2A4\uB2E5", 2),
            ("\uC99D\uAD8C", 1),
            ("\uC8FC\uC2DD", 1),
            ("ETF", 1)
        ];

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

            List<NewsItem> news = [.. result.Items
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

            return IsMarketQuery(stockName)
                ? news
                : [.. news.Where(IsCleanStockNews).Take(displayCount)];
        }

        public Task<List<NewsItem>> GetMarketNewsAsync(CancellationToken cancellationToken = default)
        {
            string query = NormalizeMarketQuery(_settings.MarketQuery);
            int count = _settings.MarketDisplayCount > 0 ? _settings.MarketDisplayCount : 20;
            return GetFilteredMarketNewsAsync(query, count, cancellationToken);
        }

        public Task<List<NewsItem>> SearchNewsAsync(string query, CancellationToken cancellationToken = default)
        {
            int count = _settings.MarketDisplayCount > 0 ? _settings.MarketDisplayCount : 20;
            return GetLatestNewsAsync(query, count, cancellationToken);
        }

        private async Task<List<NewsItem>> GetFilteredMarketNewsAsync(string query, int count, CancellationToken cancellationToken)
        {
            string[] queries = SplitMarketQueries(query);
            List<NewsItem> rawNews = [];

            foreach (string marketQuery in queries)
            {
                if (rawNews.Count > 0)
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);

                try
                {
                    rawNews.AddRange(await GetLatestNewsAsync(marketQuery, Math.Min(20, Math.Max(count, 10)), cancellationToken).ConfigureAwait(false));
                }
                catch (HttpRequestException) when (rawNews.Count > 0)
                {
                    break;
                }
            }

            List<NewsItem> rankedNews = [.. rawNews
                .GroupBy(item => string.IsNullOrWhiteSpace(item.Link) ? item.Title : item.Link)
                .Select(group => group.First())
                .Select(item => new { Item = item, Score = GetMarketNewsScore(item), PublishedAt = TryParsePubDate(item.PubDate) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.PublishedAt)
                .Select(item => item.Item)
                .Take(count)];

            return rankedNews.Count > 0
                ? rankedNews
                : [.. rawNews.Take(count)];
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string noTags = Regex.Replace(text, "<.*?>", string.Empty);
            return WebUtility.HtmlDecode(noTags).Trim();
        }

        private static string NormalizeMarketQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return DefaultMarketQuery;

            string trimmed = query.Trim();
            int questionCount = trimmed.Count(ch => ch == '?');
            bool looksCorrupted = questionCount >= 2 || trimmed.Contains('\uFFFD');
            return looksCorrupted ? DefaultMarketQuery : trimmed;
        }

        private static string[] SplitMarketQueries(string query)
        {
            return query.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .DefaultIfEmpty(DefaultMarketQuery)
                .Take(12)
                .ToArray();
        }

        private static bool IsMarketQuery(string query)
        {
            return query.Contains('|');
        }

        private static bool IsCleanStockNews(NewsItem item)
        {
            string text = $"{item.Title} {item.Description}";
            return !MarketExcludeWords.Any(text.Contains);
        }

        private static int GetMarketNewsScore(NewsItem item)
        {
            string text = $"{item.Title} {item.Description}";
            if (MarketExcludeWords.Any(text.Contains))
                return 0;

            int score = 0;
            foreach ((string word, int weight) in MarketScoreWords)
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    score += weight;
            }

            if (item.Title.Contains("[") && item.Title.Contains("]"))
                score += 1;

            return score;
        }

        private static string FormatPubDate(string pubDate)
        {
            if (string.IsNullOrWhiteSpace(pubDate))
                return string.Empty;

            return DateTimeOffset.TryParse(pubDate, out DateTimeOffset parsed)
                ? parsed.LocalDateTime.ToString("MM-dd HH:mm")
                : pubDate;
        }

        private static DateTime TryParsePubDate(string pubDate)
        {
            return DateTime.TryParse(pubDate, out DateTime parsed)
                ? parsed
                : DateTime.MinValue;
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
