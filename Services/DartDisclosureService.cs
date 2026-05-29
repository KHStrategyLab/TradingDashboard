using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Text;
using TradingDashboard.Models;

namespace TradingDashboard.Services
{
    public sealed class DartDisclosureService(DartSettings settings)
    {
        private static readonly HttpClient HttpClient = new();

        private readonly DartSettings _settings = settings ?? new DartSettings();
        private readonly string _corpCodeCachePath = ResolveCachePath();
        private Dictionary<string, string>? _corpCodeByStockCode;

        public async Task<List<DisclosureItem>> GetLatestDisclosuresAsync(string stockCode, int? count = null, CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(stockCode))
                return [];

            string normalizedStockCode = NormalizeStockCode(stockCode);
            if (string.IsNullOrWhiteSpace(normalizedStockCode))
                return [];

            Dictionary<string, string> corpCodeMap = await GetCorpCodeMapAsync(cancellationToken).ConfigureAwait(false);
            if (!corpCodeMap.TryGetValue(normalizedStockCode, out string? corpCode) || string.IsNullOrWhiteSpace(corpCode))
                return [];

            int displayCount = count ?? _settings.DisplayCount;
            if (displayCount <= 0)
                displayCount = 3;
            if (displayCount > 100)
                displayCount = 100;

            int lookbackDays = _settings.LookbackDays <= 0 ? 180 : _settings.LookbackDays;
            string beginDate = DateTime.Today.AddDays(-lookbackDays).ToString("yyyyMMdd");
            string endDate = DateTime.Today.ToString("yyyyMMdd");
            int pageCount = Math.Max(displayCount, 10);
            string url = "https://opendart.fss.or.kr/api/list.json"
                + $"?crtfc_key={Uri.EscapeDataString(_settings.ApiKey)}"
                + $"&corp_code={Uri.EscapeDataString(corpCode)}"
                + $"&bgn_de={beginDate}"
                + $"&end_de={endDate}"
                + "&sort=date&sort_mth=desc"
                + $"&page_count={pageCount}";

            using HttpResponseMessage response = await SendDartGetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            DartListResponse? result = JsonSerializer.Deserialize<DartListResponse>(json, options);
            if (result == null)
                return [];
            if (!string.IsNullOrWhiteSpace(result.Status) && result.Status != "000")
                return [];
            if (result.List == null || result.List.Count == 0)
                return [];

            return [.. result.List
                .Take(displayCount)
                .Select(item => new DisclosureItem
                {
                    ReportName = item.ReportName ?? string.Empty,
                    Title = item.ReportName ?? string.Empty,
                    DateText = FormatDate(item.ReceiptDate),
                    Link = string.IsNullOrWhiteSpace(item.ReceiptNo)
                        ? string.Empty
                        : $"https://dart.fss.or.kr/dsaf001/main.do?rcpNo={item.ReceiptNo}"
                })];
        }

        private async Task<Dictionary<string, string>> GetCorpCodeMapAsync(CancellationToken cancellationToken)
        {
            if (_corpCodeByStockCode != null)
                return _corpCodeByStockCode;

            if (File.Exists(_corpCodeCachePath))
            {
                string cachedJson = await File.ReadAllTextAsync(_corpCodeCachePath, cancellationToken).ConfigureAwait(false);
                _corpCodeByStockCode = JsonSerializer.Deserialize<Dictionary<string, string>>(cachedJson)
                                       ?? new Dictionary<string, string>(StringComparer.Ordinal);
                return _corpCodeByStockCode;
            }

            string url = $"https://opendart.fss.or.kr/api/corpCode.xml?crtfc_key={Uri.EscapeDataString(_settings.ApiKey)}";
            using HttpResponseMessage response = await SendDartGetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] zipBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (zipBytes.Length < 4 || zipBytes[0] != 'P' || zipBytes[1] != 'K')
            {
                _corpCodeByStockCode = new Dictionary<string, string>(StringComparer.Ordinal);
                return _corpCodeByStockCode;
            }

            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                _corpCodeByStockCode = new Dictionary<string, string>(StringComparer.Ordinal);
                return _corpCodeByStockCode;
            }

            using Stream entryStream = entry.Open();
            string xml;
            using (var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                xml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            XDocument doc = XDocument.Parse(xml);
            _corpCodeByStockCode = doc.Descendants("list")
                .Select(node => new
                {
                    StockCode = NormalizeStockCode(node.Element("stock_code")?.Value ?? string.Empty),
                    CorpCode = (node.Element("corp_code")?.Value ?? string.Empty).Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.StockCode) && !string.IsNullOrWhiteSpace(x.CorpCode))
                .GroupBy(x => x.StockCode, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().CorpCode, StringComparer.Ordinal);

            Directory.CreateDirectory(Path.GetDirectoryName(_corpCodeCachePath)!);
            string json = JsonSerializer.Serialize(_corpCodeByStockCode, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(_corpCodeCachePath, json, cancellationToken).ConfigureAwait(false);
            return _corpCodeByStockCode;
        }

        private static string ResolveCachePath()
        {
            string? configDir = SearchUpwards(Directory.GetCurrentDirectory(), "Config")
                                ?? SearchUpwards(AppContext.BaseDirectory, "Config");
            return Path.Combine(configDir ?? Path.Combine(Directory.GetCurrentDirectory(), "Config"), "dart_corp_codes.json");
        }

        private static Task<HttpResponseMessage> SendDartGetAsync(string url, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = new Version(1, 1)
            };
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            request.Headers.Accept.ParseAdd("application/json,application/zip,application/xml,*/*");
            return HttpClient.SendAsync(request, cancellationToken);
        }

        private static string? SearchUpwards(string startPath, string folderName)
        {
            DirectoryInfo? dir = new(startPath);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, folderName);
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        private static string NormalizeStockCode(string value)
        {
            string text = (value ?? string.Empty).Trim()
                .Replace("_NX", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_AL", string.Empty, StringComparison.OrdinalIgnoreCase);
            return text.Length >= 6 ? text[..6] : text;
        }

        private static string FormatDate(string? raw)
        {
            if (DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime parsed))
                return parsed.ToString("MM-dd");
            return raw ?? string.Empty;
        }

        private sealed class DartListResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public List<DartDisclosureRawItem> List { get; set; } = [];
        }

        private sealed class DartDisclosureRawItem
        {
            [JsonPropertyName("rcept_no")]
            public string ReceiptNo { get; set; } = string.Empty;

            [JsonPropertyName("rcept_dt")]
            public string ReceiptDate { get; set; } = string.Empty;

            [JsonPropertyName("report_nm")]
            public string ReportName { get; set; } = string.Empty;
        }
    }
}
