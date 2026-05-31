using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class NewsKeywordFilterService
    {
        private const string DefaultRelativePath = "Config/NewsKeywords/news_keywords.json";
        private const int MinimumPassScore = 4;
        private readonly Lazy<IReadOnlyList<KeywordCategory>> _categories = new(LoadCategories);

        public bool IsAvailable => _categories.Value.Count > 0;

        public IReadOnlyList<RankedNewsItem> Rank(IReadOnlyList<NewsItem> newsItems)
        {
            IReadOnlyList<KeywordCategory> categories = _categories.Value;
            if (categories.Count == 0)
                return [.. newsItems.Select((item, index) => new RankedNewsItem(item, 0, [], [], index))];

            List<RankedNewsItem> ranked = [];
            for (int i = 0; i < newsItems.Count; i++)
            {
                NewsItem item = newsItems[i];
                KeywordMatchResult match = Match(item, categories);
                int recencyScore = GetRecencyScore(item.PubDate);
                ranked.Add(new RankedNewsItem(item, match.Score + recencyScore, match.Tags, match.Words, i));
            }

            return [.. ranked
                .Where(item => item.Score >= MinimumPassScore)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.OriginalIndex)];
        }

        public IReadOnlyList<NewsItem> FilterForAlert(IReadOnlyList<NewsItem> newsItems, int count)
        {
            if (count <= 0)
                return [];

            if (!IsAvailable)
                return [];

            return [.. Rank(newsItems).Take(count).Select(item => item.Item)];
        }

        private static KeywordMatchResult Match(NewsItem item, IReadOnlyList<KeywordCategory> categories)
        {
            string title = item.Title ?? string.Empty;
            string body = item.Description ?? string.Empty;
            int score = 0;
            var tags = new HashSet<string>(StringComparer.Ordinal);
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeywordCategory category in categories)
            {
                int categoryScore = 0;
                foreach (string keyword in category.Keywords)
                {
                    bool titleHit = Contains(title, keyword);
                    bool bodyHit = Contains(body, keyword);
                    if (!titleHit && !bodyHit)
                        continue;

                    words.Add(keyword);
                    categoryScore += category.Weight * (titleHit ? 2 : 0);
                    categoryScore += category.Weight * (bodyHit ? 1 : 0);
                }

                if (categoryScore <= 0)
                    continue;

                if (IsPositiveSentiment(category.Sentiment))
                {
                    score += Math.Min(categoryScore, category.Weight * 6);
                }
                else
                {
                    score -= Math.Min(categoryScore, 2);
                }

                tags.Add(category.Name);
                foreach (string tag in category.Tags)
                    tags.Add(tag);
            }

            return new KeywordMatchResult(score, [.. tags.Take(8)], [.. words.Take(12)]);
        }

        private static bool IsPositiveSentiment(string sentiment)
        {
            return sentiment.Equals("positive", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetRecencyScore(string pubDate)
        {
            if (string.IsNullOrWhiteSpace(pubDate))
                return 0;

            if (!DateTime.TryParse(pubDate, out DateTime parsed))
                return 0;

            DateTime today = DateTime.Today;
            if (parsed.Date == today)
                return 2;

            return parsed.Date == today.AddDays(-1) ? 1 : 0;
        }

        private static bool Contains(string text, string keyword)
        {
            return !string.IsNullOrWhiteSpace(text)
                && !string.IsNullOrWhiteSpace(keyword)
                && text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<KeywordCategory> LoadCategories()
        {
            string path = ResolveKeywordPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return [];

            try
            {
                using FileStream stream = File.OpenRead(path);
                using JsonDocument doc = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                if (!doc.RootElement.TryGetProperty("categories", out JsonElement categoriesElement) ||
                    categoriesElement.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                List<KeywordCategory> categories = [];
                foreach (JsonElement category in categoriesElement.EnumerateArray())
                {
                    string id = ReadString(category, "id");
                    string name = ReadString(category, "name");
                    string sentiment = ReadString(category, "sentiment");
                    string grade = ReadString(category, "grade");
                    string[] tags = ReadStringArray(category, "tags");
                    string[] keywords = [.. ReadStringArray(category, "keywords")
                        .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                        .Select(keyword => keyword.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(keyword => keyword.Length)];

                    if (keywords.Length == 0)
                        continue;

                    categories.Add(new KeywordCategory(
                        id,
                        string.IsNullOrWhiteSpace(name) ? id : name,
                        sentiment,
                        grade,
                        tags,
                        keywords,
                        ResolveWeight(sentiment, grade)));
                }

                return categories;
            }
            catch
            {
                return [];
            }
        }

        private static int ResolveWeight(string sentiment, string grade)
        {
            int weight = grade switch
            {
                "strong" => 6,
                "medium" => 3,
                "weak" => 1,
                _ => 2
            };

            return weight;
        }

        private static string[] ReadStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))];
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string ResolveKeywordPath()
        {
            string basePath = Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
            if (File.Exists(basePath))
                return basePath;

            string currentPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultRelativePath);
            if (File.Exists(currentPath))
                return currentPath;

            string foundFromBase = SearchUpwards(AppContext.BaseDirectory, DefaultRelativePath);
            if (!string.IsNullOrWhiteSpace(foundFromBase))
                return foundFromBase;

            string foundFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), DefaultRelativePath);
            return string.IsNullOrWhiteSpace(foundFromCurrent) ? currentPath : foundFromCurrent;
        }

        private static string SearchUpwards(string startDirectory, string relativePath)
        {
            DirectoryInfo? directory = new(startDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            return string.Empty;
        }

        private sealed record KeywordCategory(
            string Id,
            string Name,
            string Sentiment,
            string Grade,
            IReadOnlyList<string> Tags,
            IReadOnlyList<string> Keywords,
            int Weight);

        private sealed record KeywordMatchResult(int Score, IReadOnlyList<string> Tags, IReadOnlyList<string> Words);

        public sealed record RankedNewsItem(
            NewsItem Item,
            int Score,
            IReadOnlyList<string> Tags,
            IReadOnlyList<string> Words,
            int OriginalIndex);
    }
}
