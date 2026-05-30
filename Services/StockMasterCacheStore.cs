using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class StockMasterCacheStore
    {
        private const string RelativePath = "Config/stock_master_cache.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly string _path;

        public StockMasterCacheStore(string? path = null)
        {
            _path = string.IsNullOrWhiteSpace(path) ? ResolveDefaultPath() : path;
        }

        public async Task<StockMasterCacheDocument?> LoadFreshAsync(string tradingDate, CancellationToken cancellationToken = default)
        {
            StockMasterCacheDocument? document = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (document?.Items is not { Count: > 0 })
                return null;

            if (string.IsNullOrWhiteSpace(tradingDate))
                return null;

            string cachedTradingDate = string.IsNullOrWhiteSpace(document.Meta.TradingDate)
                ? document.Meta.UpdatedAt.Length >= 8 ? document.Meta.UpdatedAt[..8] : string.Empty
                : document.Meta.TradingDate;

            return string.Equals(cachedTradingDate, tradingDate, StringComparison.Ordinal)
                ? document
                : null;
        }

        public async Task<StockMasterCacheDocument?> LoadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_path))
                    return null;

                string json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<StockMasterCacheDocument>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveAsync(StockMasterCacheDocument document, CancellationToken cancellationToken = default)
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(document, JsonOptions);
            await File.WriteAllTextAsync(_path, json, cancellationToken).ConfigureAwait(false);
        }

        private static string ResolveDefaultPath()
        {
            string? foundFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(foundFromCurrent))
                return Path.Combine(foundFromCurrent, "stock_master_cache.json");

            string? foundFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(foundFromBase))
                return Path.Combine(foundFromBase, "stock_master_cache.json");

            return Path.Combine(Directory.GetCurrentDirectory(), RelativePath);
        }

        private static string? SearchUpwards(string startDirectory, string childDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, childDirectory);
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }

            return null;
        }
    }
}
