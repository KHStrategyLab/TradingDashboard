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
            return $"{safePrefix}_{DateTime.Now:yyyyMMddHHmmss}";
        }

        public string SaveRun(
            string runId,
            IEnumerable<BacktestSignalRow> signals,
            IEnumerable<BacktestTradeRow> trades,
            IEnumerable<BacktestRunSummary> summaries)
        {
            string resolvedRunId = string.IsNullOrWhiteSpace(runId) ? CreateRunId() : SanitizeFileName(runId);
            string directory = Path.Combine(_rootPath, resolvedRunId);
            Directory.CreateDirectory(directory);

            List<BacktestSignalRow> signalRows = [.. signals ?? []];
            List<BacktestTradeRow> tradeRows = [.. trades ?? []];
            List<BacktestRunSummary> summaryRows = [.. summaries ?? []];

            File.WriteAllText(Path.Combine(directory, "signals.csv"), BuildSignalsCsv(signalRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "trades.csv"), BuildTradesCsv(tradeRows), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "summaries.json"), JsonSerializer.Serialize(summaryRows, JsonOptions), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "strategy_comparison.csv"), BuildSummaryCsv(summaryRows), Encoding.UTF8);

            return directory;
        }

        private static string BuildSignalsCsv(IEnumerable<BacktestSignalRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RunId,StrategyCode,Code,Market,SignalTime,SignalType,Price,Reason");
            foreach (BacktestSignalRow row in rows)
            {
                AppendCsvLine(sb,
                    row.RunId,
                    row.StrategyCode,
                    row.Code,
                    row.Market,
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
            sb.AppendLine("RunId,StrategyCode,ExitRuleCode,Code,Market,EntryTime,ExitTime,EntryPrice,ExitPrice,MaxHigh,MinLow,Quantity,ProfitRate,ProfitAmount,MAE,MFE,HoldingMinutes,EntryReason,ExitReason");
            foreach (BacktestTradeRow row in rows)
            {
                AppendCsvLine(sb,
                    row.RunId,
                    row.StrategyCode,
                    row.ExitRuleCode,
                    row.Code,
                    row.Market,
                    row.EntryTime,
                    row.ExitTime,
                    row.EntryPrice.ToString(CultureInfo.InvariantCulture),
                    row.ExitPrice.ToString(CultureInfo.InvariantCulture),
                    row.MaxHigh.ToString(CultureInfo.InvariantCulture),
                    row.MinLow.ToString(CultureInfo.InvariantCulture),
                    row.Quantity.ToString(CultureInfo.InvariantCulture),
                    row.ProfitRate.ToString(CultureInfo.InvariantCulture),
                    row.ProfitAmount.ToString(CultureInfo.InvariantCulture),
                    row.Mae.ToString(CultureInfo.InvariantCulture),
                    row.Mfe.ToString(CultureInfo.InvariantCulture),
                    row.HoldingMinutes.ToString(CultureInfo.InvariantCulture),
                    row.EntryReason,
                    row.ExitReason);
            }

            return sb.ToString();
        }

        private static string BuildSummaryCsv(IEnumerable<BacktestRunSummary> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("StrategyCode,ExitRuleCode,SignalCount,TradeCount,WinRate,AvgProfit,AvgLoss,Expectancy,TotalProfit,MaxDrawdown,MAE,MFE,AvgHoldingMinutes,ConsecutiveLosses,FeeAdjustedProfit,SlippageAdjustedProfit");
            foreach (BacktestRunSummary row in rows)
            {
                AppendCsvLine(sb,
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
