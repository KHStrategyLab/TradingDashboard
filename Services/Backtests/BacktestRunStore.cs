using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public sealed class BacktestRunStore
    {
        private const string RelativeRoot = "Storage/Backtests/Runs";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _rootPath;

        public BacktestRunStore(string? rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? ResolveDefaultRootPath() : rootPath;
        }

        public string CreateRunId(string prefix = "run")
        {
            string safePrefix = SanitizeFileName(string.IsNullOrWhiteSpace(prefix) ? "run" : prefix);
            if (!safePrefix.StartsWith("SORON_", StringComparison.OrdinalIgnoreCase))
                safePrefix = $"SORON_{safePrefix}";
            return $"{safePrefix}_{DateTime.Now:yyyyMMddHHmmss}";
        }

        public string SaveRun(
            string runId,
            IEnumerable<BacktestSignalRow> signals,
            IEnumerable<BacktestTradeRow> trades,
            IEnumerable<BacktestRunSummary> summaries,
            BacktestRunConfig? runConfig = null)
        {
            string resolvedRunId = string.IsNullOrWhiteSpace(runId) ? CreateRunId() : SanitizeFileName(runId);
            string directory = Path.Combine(_rootPath, resolvedRunId);
            Directory.CreateDirectory(directory);

            List<BacktestSignalRow> signalRows = [.. signals ?? []];
            List<BacktestTradeRow> tradeRows = [.. trades ?? []];
            List<BacktestRunSummary> summaryRows = [.. summaries ?? []];
            BacktestRunConfig resolvedConfig = BuildRunConfig(resolvedRunId, signalRows, tradeRows, summaryRows, runConfig);
            ApplyRunMetadata(resolvedConfig, signalRows, tradeRows, summaryRows);

            File.WriteAllText(Path.Combine(directory, "signals.csv"), BuildSignalsCsv(signalRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "trades.csv"), BuildTradesCsv(tradeRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "summaries.json"), JsonSerializer.Serialize(summaryRows, JsonOptions), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "summary.json"), BuildSummaryJson(resolvedRunId, summaryRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "strategy_comparison.csv"), BuildSummaryCsv(summaryRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "samples.csv"), BuildSamplesCsv(tradeRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "run_config.json"), JsonSerializer.Serialize(resolvedConfig, JsonOptions), Encoding.UTF8);

            return directory;
        }

        private static BacktestRunConfig BuildRunConfig(
            string runId,
            IReadOnlyCollection<BacktestSignalRow> signals,
            IReadOnlyCollection<BacktestTradeRow> trades,
            IReadOnlyCollection<BacktestRunSummary> summaries,
            BacktestRunConfig? config)
        {
            BacktestRunConfig resolved = config ?? new BacktestRunConfig();
            resolved.RunId = runId;
            resolved.BacktestMode = string.IsNullOrWhiteSpace(resolved.BacktestMode) ? "SOR_ON" : resolved.BacktestMode;
            resolved.RunMode = string.IsNullOrWhiteSpace(resolved.RunMode) ? resolved.BacktestMode : resolved.RunMode;
            resolved.MarketMode = string.IsNullOrWhiteSpace(resolved.MarketMode)
                ? InferMarketMode(signals.Select(item => item.Market)
                    .Concat(trades.Select(item => item.Market))
                    .Concat(summaries.Select(item => item.Market)))
                : resolved.MarketMode;
            resolved.OrderMode = string.IsNullOrWhiteSpace(resolved.OrderMode) ? "None" : resolved.OrderMode;
            resolved.ExecutionType = string.IsNullOrWhiteSpace(resolved.ExecutionType) ? "BacktestOnly" : resolved.ExecutionType;
            resolved.LiveOrder = false;
            resolved.StrategyCodes = [.. summaries
                .Select(item => item.StrategyCode)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)];
            resolved.ExitRuleCodes = [.. summaries
                .Select(item => item.ExitRuleCode)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)];
            return resolved;
        }

        private static void ApplyRunMetadata(
            BacktestRunConfig config,
            IReadOnlyList<BacktestSignalRow> signals,
            IReadOnlyList<BacktestTradeRow> trades,
            IReadOnlyList<BacktestRunSummary> summaries)
        {
            string runMode = string.IsNullOrWhiteSpace(config.RunMode) ? config.BacktestMode : config.RunMode;
            string marketMode = string.IsNullOrWhiteSpace(config.MarketMode) ? "UNKNOWN" : config.MarketMode;
            string inferredMarket = InferMarket(signals.Select(item => item.Market).Concat(trades.Select(item => item.Market)));

            foreach (BacktestSignalRow row in signals)
            {
                row.RunId = config.RunId;
                row.RunMode = runMode;
                row.MarketMode = marketMode;
                row.Market = NormalizeMarketLabel(row.Market);
                EnsureKnownMarket(row.Market, "signal", row.Code, row.SignalTime);
                row.Status = string.IsNullOrWhiteSpace(row.Status) ? "Generated" : row.Status;
            }

            foreach (BacktestTradeRow row in trades)
            {
                row.RunId = config.RunId;
                row.RunMode = runMode;
                row.MarketMode = marketMode;
                row.Market = NormalizeMarketLabel(row.Market);
                EnsureKnownMarket(row.Market, "trade", row.Code, row.EntryTime);
                row.Status = string.IsNullOrWhiteSpace(row.Status) ? "Completed" : row.Status;
            }

            foreach (BacktestRunSummary row in summaries)
            {
                row.RunId = config.RunId;
                row.RunMode = runMode;
                row.MarketMode = marketMode;
                row.Market = string.IsNullOrWhiteSpace(row.Market) ? inferredMarket : NormalizeMarketLabel(row.Market);
                row.Status = string.IsNullOrWhiteSpace(row.Status) ? "Completed" : row.Status;
            }
        }

        private static string BuildSummaryJson(string runId, IReadOnlyList<BacktestRunSummary> summaries)
        {
            object payload = summaries.Count == 1
                ? summaries[0]
                : new
                {
                    RunId = runId,
                    Summaries = summaries
                };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static string BuildSignalsCsv(IEnumerable<BacktestSignalRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RunId,RunMode,MarketMode,StrategyCode,Code,Market,Status,SignalTime,SignalType,Price,Reason");
            foreach (BacktestSignalRow row in rows)
            {
                AppendCsvLine(sb,
                    row.RunId,
                    row.RunMode,
                    row.MarketMode,
                    row.StrategyCode,
                    row.Code,
                    row.Market,
                    row.Status,
                    row.SignalTime,
                    row.SignalType,
                    row.Price.ToString(CultureInfo.InvariantCulture),
                    row.Reason);
            }

            return sb.ToString();
        }

        private static string BuildTradesCsv(IEnumerable<BacktestTradeRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RunId,RunMode,MarketMode,StrategyCode,ExitRuleCode,Code,Market,Status,EntryTime,ExitTime,EntryPrice,ExitPrice,MaxHigh,MinLow,StopPrice,Quantity,ProfitRate,ProfitAmount,MAE,MFE,RiskRate,MaxR,MinR,HoldingMinutes,EntryReason,ExitReason");
            foreach (BacktestTradeRow row in rows)
            {
                AppendCsvLine(sb,
                    row.RunId,
                    row.RunMode,
                    row.MarketMode,
                    row.StrategyCode,
                    row.ExitRuleCode,
                    row.Code,
                    row.Market,
                    row.Status,
                    row.EntryTime,
                    row.ExitTime,
                    row.EntryPrice.ToString(CultureInfo.InvariantCulture),
                    row.ExitPrice.ToString(CultureInfo.InvariantCulture),
                    row.MaxHigh.ToString(CultureInfo.InvariantCulture),
                    row.MinLow.ToString(CultureInfo.InvariantCulture),
                    row.StopPrice.ToString(CultureInfo.InvariantCulture),
                    row.Quantity.ToString(CultureInfo.InvariantCulture),
                    row.ProfitRate.ToString(CultureInfo.InvariantCulture),
                    row.ProfitAmount.ToString(CultureInfo.InvariantCulture),
                    row.Mae.ToString(CultureInfo.InvariantCulture),
                    row.Mfe.ToString(CultureInfo.InvariantCulture),
                    row.RiskRate.ToString(CultureInfo.InvariantCulture),
                    row.MaxR.ToString(CultureInfo.InvariantCulture),
                    row.MinR.ToString(CultureInfo.InvariantCulture),
                    row.HoldingMinutes.ToString(CultureInfo.InvariantCulture),
                    row.EntryReason,
                    row.ExitReason);
            }

            return sb.ToString();
        }

        private static string BuildSummaryCsv(IEnumerable<BacktestRunSummary> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RunId,RunMode,MarketMode,Market,Status,StrategyCode,ExitRuleCode,SignalCount,TradeCount,WinRate,AvgProfit,AvgLoss,Expectancy,TotalProfit,MaxDrawdown,MAE,MFE,AvgHoldingMinutes,ConsecutiveLosses,FeeAdjustedProfit,SlippageAdjustedProfit");
            foreach (BacktestRunSummary row in rows)
            {
                AppendCsvLine(sb,
                    row.RunId,
                    row.RunMode,
                    row.MarketMode,
                    row.Market,
                    row.Status,
                    row.StrategyCode,
                    row.ExitRuleCode,
                    row.SignalCount.ToString(CultureInfo.InvariantCulture),
                    row.TradeCount.ToString(CultureInfo.InvariantCulture),
                    row.WinRate.ToString(CultureInfo.InvariantCulture),
                    row.AvgProfit.ToString(CultureInfo.InvariantCulture),
                    row.AvgLoss.ToString(CultureInfo.InvariantCulture),
                    row.Expectancy.ToString(CultureInfo.InvariantCulture),
                    row.TotalProfit.ToString(CultureInfo.InvariantCulture),
                    row.MaxDrawdown.ToString(CultureInfo.InvariantCulture),
                    row.MAE.ToString(CultureInfo.InvariantCulture),
                    row.MFE.ToString(CultureInfo.InvariantCulture),
                    row.AvgHoldingMinutes.ToString(CultureInfo.InvariantCulture),
                    row.ConsecutiveLosses.ToString(CultureInfo.InvariantCulture),
                    row.FeeAdjustedProfit.ToString(CultureInfo.InvariantCulture),
                    row.SlippageAdjustedProfit.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static string BuildSamplesCsv(IReadOnlyList<BacktestTradeRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SampleType,RunId,RunMode,MarketMode,StrategyCode,ExitRuleCode,Code,Market,Status,EntryTime,ExitTime,EntryPrice,ExitPrice,ProfitRate,MAE,MFE,MaxR,MinR,HoldingMinutes,EntryReason,ExitReason");

            IEnumerable<BacktestTradeRow> good = rows
                .Where(row => row.ProfitRate > 0)
                .OrderByDescending(row => row.ProfitRate)
                .Take(10);
            IEnumerable<BacktestTradeRow> failed = rows
                .Where(row => row.ProfitRate < 0)
                .OrderBy(row => row.ProfitRate)
                .Take(10);
            IEnumerable<BacktestTradeRow> ambiguous = rows
                .Where(row => row.ProfitRate == 0)
                .Take(10);

            foreach ((string sampleType, BacktestTradeRow row) in good.Select(row => ("GOOD", row))
                .Concat(failed.Select(row => ("FAILED", row)))
                .Concat(ambiguous.Select(row => ("AMBIGUOUS", row))))
            {
                AppendCsvLine(sb,
                    sampleType,
                    row.RunId,
                    row.RunMode,
                    row.MarketMode,
                    row.StrategyCode,
                    row.ExitRuleCode,
                    row.Code,
                    row.Market,
                    row.Status,
                    row.EntryTime,
                    row.ExitTime,
                    row.EntryPrice.ToString(CultureInfo.InvariantCulture),
                    row.ExitPrice.ToString(CultureInfo.InvariantCulture),
                    row.ProfitRate.ToString(CultureInfo.InvariantCulture),
                    row.Mae.ToString(CultureInfo.InvariantCulture),
                    row.Mfe.ToString(CultureInfo.InvariantCulture),
                    row.MaxR.ToString(CultureInfo.InvariantCulture),
                    row.MinR.ToString(CultureInfo.InvariantCulture),
                    row.HoldingMinutes.ToString(CultureInfo.InvariantCulture),
                    row.EntryReason,
                    row.ExitReason);
            }

            return sb.ToString();
        }

        private static void AppendCsvLine(StringBuilder sb, params string[] cells)
        {
            sb.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
                return text;

            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        private static string InferMarketMode(IEnumerable<string> markets)
        {
            string[] distinct = [.. markets
                .Select(NormalizeMarketLabel)
                .Where(item => item is "KRX" or "NXT" or "AL")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)];

            return distinct switch
            {
                ["KRX"] => "KRX_ONLY",
                ["NXT"] => "NXT_ONLY",
                ["AL"] => "AL_ONLY",
                ["KRX", "NXT"] => "MARKET_SPLIT",
                _ => "UNKNOWN"
            };
        }

        private static string InferMarket(IEnumerable<string> markets)
        {
            string[] distinct = [.. markets
                .Select(NormalizeMarketLabel)
                .Where(item => item is "KRX" or "NXT" or "AL")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)];

            return distinct.Length switch
            {
                0 => "UNKNOWN",
                1 => distinct[0],
                _ => "MIXED"
            };
        }

        private static string NormalizeMarketLabel(string market)
        {
            string text = (market ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Contains("AL", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("SOR", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("UNIFIED", StringComparison.OrdinalIgnoreCase))
                return "AL";
            if (text.Contains("NXT", StringComparison.OrdinalIgnoreCase))
                return "NXT";
            if (text.Contains("KRX", StringComparison.OrdinalIgnoreCase))
                return "KRX";
            if (text.Contains("MIX", StringComparison.OrdinalIgnoreCase))
                return "MIXED";
            return string.IsNullOrWhiteSpace(text) ? "UNKNOWN" : text;
        }

        private static void EnsureKnownMarket(string market, string rowType, string code, string time)
        {
            if (market is "KRX" or "NXT" or "AL")
                return;

            throw new InvalidOperationException(
                $"Backtest {rowType} row has unknown market. Code={code}, Time={time}, Market={market}");
        }

        private static string ResolveDefaultRootPath()
        {
            string? configFromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(configFromCurrent))
                return Path.Combine(Directory.GetParent(configFromCurrent)?.FullName ?? Directory.GetCurrentDirectory(), RelativeRoot.Replace('/', Path.DirectorySeparatorChar));

            string? configFromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(configFromBase))
                return Path.Combine(Directory.GetParent(configFromBase)?.FullName ?? AppContext.BaseDirectory, RelativeRoot.Replace('/', Path.DirectorySeparatorChar));

            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot.Replace('/', Path.DirectorySeparatorChar));
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

        private static string SanitizeFileName(string value)
        {
            string text = string.Join("_", (value ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(text) ? "run" : text;
        }
    }
}
