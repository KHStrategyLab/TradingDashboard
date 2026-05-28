using System.IO;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class WatchlistStockCacheStore
    {
        private const string RelativePath = "Config/watchlist_stock_cache.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string CachePath { get; } = ResolvePath();

        public List<WatchlistStockCacheEntry> Load()
        {
            if (!File.Exists(CachePath))
                return new List<WatchlistStockCacheEntry>();

            string json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<List<WatchlistStockCacheEntry>>(json, JsonOptions)
                   ?? new List<WatchlistStockCacheEntry>();
        }

        public void Save(IEnumerable<WatchlistStockCacheEntry> entries)
        {
            string? directory = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(entries.OrderBy(e => e.Code).ToList(), JsonOptions);
            File.WriteAllText(CachePath, json);
        }

        private static string ResolvePath()
        {
            string foundFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(foundFromCurrent))
                return Path.Combine(foundFromCurrent, "watchlist_stock_cache.json");

            string foundFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(foundFromBase))
                return Path.Combine(foundFromBase, "watchlist_stock_cache.json");

            return Path.Combine(Directory.GetCurrentDirectory(), RelativePath);
        }

        private static string SearchUpwards(string startDirectory, string childDirectory)
        {
            DirectoryInfo? directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, childDirectory);
                if (Directory.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            return string.Empty;
        }
    }
}
