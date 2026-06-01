using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestCandidateImportService
    {
        private static readonly Regex CodeRegex = new(@"(?<!\d)A?\d{6}(?!\d)", RegexOptions.Compiled);

        public IReadOnlyList<BacktestCandidate> LoadFromWatchlistCache(
            WatchlistStockCacheStore? cacheStore = null,
            string sourceName = "watchlist_stock_cache")
        {
            cacheStore ??= new WatchlistStockCacheStore();
            List<WatchlistStockCacheEntry> entries = cacheStore.Load();
            DateTime importedAt = DateTime.Now;

            return [.. entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Code))
                .Select(entry =>
                {
                    string market = !string.IsNullOrWhiteSpace(entry.GateBaseCandleMarket)
                        ? entry.GateBaseCandleMarket
                        : entry.Market;
                    string candidateDate = !string.IsNullOrWhiteSpace(entry.LastSeenConditionDate)
                        ? entry.LastSeenConditionDate
                        : !string.IsNullOrWhiteSpace(entry.SnapshotDate) ? entry.SnapshotDate : entry.GateBaseCandleCheckedDate;

                    return new BacktestCandidate
                    {
                        Code = BacktestDataStore.NormalizeCode(entry.Code),
                        Name = entry.Name,
                        Market = BacktestDataStore.NormalizeMarket(market),
                        CandidateDate = BacktestDataStore.NormalizeDate(candidateDate),
                        NxtEnabled = entry.SupportsNxt,
                        StrategyCode = "BASE_CANDLE",
                        Source = "WATCHLIST_CACHE",
                        SourceName = sourceName,
                        Memo = BuildWatchlistMemo(entry),
                        ImportedAt = importedAt
                    };
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{item.Code}|{item.Market}|{item.CandidateDate}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Code)];
        }

        public IReadOnlyList<BacktestCandidate> LoadCandidates(string path, string sourceName = "")
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return [];

            string extension = Path.GetExtension(path).ToLowerInvariant();
            string resolvedSourceName = string.IsNullOrWhiteSpace(sourceName)
                ? Path.GetFileNameWithoutExtension(path)
                : sourceName;

            IReadOnlyList<BacktestCandidate> candidates = extension switch
            {
                ".json" => LoadFromJson(path, resolvedSourceName),
                ".csv" or ".tsv" or ".txt" => LoadFromDelimitedText(File.ReadAllText(path, DetectEncoding(path)), resolvedSourceName),
                ".xlsx" => LoadFromDelimitedText(ReadXlsxText(path), resolvedSourceName),
                ".xls" => LoadFromExcelLikeBinary(path, resolvedSourceName),
                _ => []
            };

            return [.. candidates
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => $"{item.Code}|{item.Market}|{item.CandidateDate}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.Code)];
        }

        private static IReadOnlyList<BacktestCandidate> LoadFromJson(string path, string sourceName)
        {
            try
            {
                string json = File.ReadAllText(path);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return [];

                List<BacktestCandidate> candidates = [];
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    string code = ReadJsonString(item, "Code", "code", "StockCode", "stockCode", "종목코드");
                    if (string.IsNullOrWhiteSpace(code))
                        continue;

                    candidates.Add(new BacktestCandidate
                    {
                        Code = BacktestDataStore.NormalizeCode(code),
                        Name = ReadJsonString(item, "Name", "name", "StockName", "stockName", "종목명"),
                        Market = BacktestDataStore.NormalizeMarket(ReadJsonString(item, "Market", "market")),
                        CandidateDate = BacktestDataStore.NormalizeDate(ReadJsonString(item, "CandidateDate", "candidateDate", "LastSeenConditionDate", "SnapshotDate")),
                        NxtEnabled = ReadJsonBool(item, "NxtEnabled", "SupportsNxt", "supportsNxt"),
                        StrategyCode = ReadJsonString(item, "StrategyCode", "strategyCode") is { Length: > 0 } strategy ? strategy : "BASE_CANDLE",
                        Source = "JSON",
                        SourceName = sourceName,
                        Memo = ReadJsonString(item, "Memo", "memo"),
                        ImportedAt = DateTime.Now
                    });
                }

                return candidates;
            }
            catch
            {
                return [];
            }
        }

        private static IReadOnlyList<BacktestCandidate> LoadFromDelimitedText(string text, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                return ExtractCandidatesFromFreeText(text, sourceName);

            char delimiter = lines[0].Contains('\t') ? '\t' : ',';
            string[] headers = SplitLine(lines[0], delimiter);
            bool hasHeader = headers.Any(IsCodeHeader);
            if (!hasHeader)
                return ExtractCandidatesFromFreeText(text, sourceName);

            int codeIndex = FindHeader(headers, "Code", "StockCode", "종목코드", "단축코드");
            int nameIndex = FindHeader(headers, "Name", "StockName", "종목명", "한글명");
            int marketIndex = FindHeader(headers, "Market", "시장");
            int dateIndex = FindHeader(headers, "CandidateDate", "Date", "편입일", "검색일", "일자");

            List<BacktestCandidate> candidates = [];
            foreach (string line in lines.Skip(1))
            {
                string[] cells = SplitLine(line, delimiter);
                string code = GetCell(cells, codeIndex);
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                candidates.Add(new BacktestCandidate
                {
                    Code = BacktestDataStore.NormalizeCode(code),
                    Name = GetCell(cells, nameIndex),
                    Market = BacktestDataStore.NormalizeMarket(GetCell(cells, marketIndex)),
                    CandidateDate = BacktestDataStore.NormalizeDate(GetCell(cells, dateIndex)),
                    Source = "EXCEL",
                    SourceName = sourceName,
                    StrategyCode = "BASE_CANDLE",
                    ImportedAt = DateTime.Now
                });
            }

            return candidates;
        }

        private static IReadOnlyList<BacktestCandidate> LoadFromExcelLikeBinary(string path, string sourceName)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            byte[] bytes = File.ReadAllBytes(path);
            string text = Encoding.GetEncoding(949).GetString(bytes);
            IReadOnlyList<BacktestCandidate> delimited = LoadFromDelimitedText(text, sourceName);
            return delimited.Count > 0 ? delimited : ExtractCandidatesFromFreeText(text, sourceName);
        }

        private static IReadOnlyList<BacktestCandidate> ExtractCandidatesFromFreeText(string text, string sourceName)
        {
            List<BacktestCandidate> candidates = [];
            foreach (Match match in CodeRegex.Matches(text ?? string.Empty))
            {
                string code = BacktestDataStore.NormalizeCode(match.Value);
                if (code.Length != 6)
                    continue;

                candidates.Add(new BacktestCandidate
                {
                    Code = code,
                    Market = "KRX",
                    CandidateDate = DateTime.Today.ToString("yyyyMMdd"),
                    Source = "EXCEL_TEXT",
                    SourceName = sourceName,
                    StrategyCode = "BASE_CANDLE",
                    ImportedAt = DateTime.Now
                });
            }

            return candidates;
        }

        private static string ReadXlsxText(string path)
        {
            try
            {
                using ZipArchive archive = ZipFile.OpenRead(path);
                List<string> parts = [];
                foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    using Stream stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    parts.Add(reader.ReadToEnd());
                }

                return string.Join('\n', parts);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Encoding DetectEncoding(string path)
        {
            byte[] prefix = File.ReadAllBytes(path).Take(3).ToArray();
            if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
                return Encoding.UTF8;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949);
        }

        private static string[] SplitLine(string line, char delimiter) =>
            (line ?? string.Empty).Split(delimiter).Select(cell => cell.Trim().Trim('"')).ToArray();

        private static bool IsCodeHeader(string value) =>
            string.Equals(value, "Code", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "StockCode", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("종목코드", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("단축코드", StringComparison.OrdinalIgnoreCase);

        private static int FindHeader(string[] headers, params string[] names)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                foreach (string name in names)
                {
                    if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase) ||
                        headers[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        private static string GetCell(string[] cells, int index) =>
            index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;

        private static string ReadJsonString(JsonElement item, params string[] names)
        {
            foreach (string name in names)
            {
                if (item.TryGetProperty(name, out JsonElement value))
                    return value.ToString();
            }

            return string.Empty;
        }

        private static bool ReadJsonBool(JsonElement item, params string[] names)
        {
            foreach (string name in names)
            {
                if (!item.TryGetProperty(name, out JsonElement value))
                    continue;
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return value.GetBoolean();
                if (bool.TryParse(value.ToString(), out bool parsed))
                    return parsed;
            }

            return false;
        }

        private static string BuildWatchlistMemo(WatchlistStockCacheEntry entry)
        {
            List<string> parts = [];
            if (entry.GateBaseCandleFound)
                parts.Add($"gate {entry.GateBaseCandleDate} {entry.GateBaseCandleChangeRate:0.##}% {entry.GateBaseCandleTradeValue:N0}");
            if (!string.IsNullOrWhiteSpace(entry.StockState))
                parts.Add(entry.StockState);
            if (!string.IsNullOrWhiteSpace(entry.AuditInfo))
                parts.Add(entry.AuditInfo);
            return string.Join(" / ", parts);
        }
    }
}
