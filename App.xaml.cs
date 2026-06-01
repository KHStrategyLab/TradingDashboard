using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using TradingDashboard.Models;
using TradingDashboard.Services;
using TradingDashboard.Services.Backtests;

namespace TradingDashboard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Any(arg => string.Equals(arg, "--backtest-daily-datastore", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunBacktestDailyDataStoreJobAsync().ConfigureAwait(true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-minute-datastore", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunBacktestMinuteDataStoreJobAsync().ConfigureAwait(true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-ten-pullback-five-breakout", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunTenPullbackFiveBreakoutBacktest(6);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-ten-pullback-five-breakout-3h", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunTenPullbackFiveBreakoutBacktest(36);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-center-10m-3m", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseCenterPullbackBacktest(baseMinute: 10, entryMinute: 3, useOneMinuteTrigger: false);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-center-10m-3m-1m", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseCenterPullbackBacktest(baseMinute: 10, entryMinute: 3, useOneMinuteTrigger: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-center-15m-5m-1m", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseCenterPullbackBacktest(baseMinute: 15, entryMinute: 5, useOneMinuteTrigger: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            base.OnStartup(e);
        }

        private static async Task<int> RunBacktestDailyDataStoreJobAsync()
        {
            try
            {
                AppConfig config = LocalSettingsLoader.Load();
                var kiwoomService = new KiwoomRestConditionService(config.Kiwoom);
                var job = new BacktestDailyDataStoreJob(kiwoomService, config.Backtest);
                BacktestDatasetUpdateSummary summary = await job.RunAsync().ConfigureAwait(false);
                WriteBacktestJobSummary(summary);
                return 0;
            }
            catch (Exception ex)
            {
                WriteBacktestJobSummary(new BacktestDatasetUpdateSummary
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Logs = [$"backtest daily datastore failed: {ex.GetType().Name}: {ex.Message}"]
                });
                return 1;
            }
        }

        private static async Task<int> RunBacktestMinuteDataStoreJobAsync()
        {
            try
            {
                AppConfig config = LocalSettingsLoader.Load();
                var kiwoomService = new KiwoomRestConditionService(config.Kiwoom);
                var job = new BacktestMinuteDataStoreJob(kiwoomService, config.Backtest);
                BacktestMinuteDataStoreSummary summary = await job.RunAsync().ConfigureAwait(false);
                WriteBacktestJobSummary(summary, "last_minute_update_summary.json", $"minute_update_summary_{summary.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new BacktestMinuteDataStoreSummary
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Logs = [$"backtest minute datastore failed: {ex.GetType().Name}: {ex.Message}"]
                };
                WriteBacktestJobSummary(summary, "last_minute_update_summary.json", $"minute_update_summary_{summary.RunId}.json");
                return 1;
            }
        }

        private static void WriteBacktestJobSummary(BacktestDatasetUpdateSummary summary)
        {
            WriteBacktestJobSummary(summary, "last_daily_update_summary.json", $"daily_update_summary_{summary.RunId}.json");
        }

        private static int RunTenPullbackFiveBreakoutBacktest(int holdingBars)
        {
            try
            {
                var backtest = new TenMinutePullbackFiveMinuteBreakoutBacktest();
                BacktestRunResult result = backtest.Run(holdingBars);
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest strategy failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunSmallBaseCenterPullbackBacktest(int baseMinute, int entryMinute, bool useOneMinuteTrigger)
        {
            try
            {
                var backtest = new SmallBaseCenterPullbackBacktest();
                BacktestRunResult result = backtest.Run(
                    baseMinute: baseMinute,
                    entryMinute: entryMinute,
                    triggerMinute: useOneMinuteTrigger ? 1 : 0,
                    observationMinutes: 180);
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest small-base center pullback failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static void WriteBacktestJobSummary<T>(T summary, string latestFileName, string runFileName)
        {
            string root = ResolveProjectRoot();
            string directory = Path.Combine(root, "Storage", "Backtests", "DataStore", "metadata");
            Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(directory, latestFileName), json);
            File.WriteAllText(Path.Combine(directory, runFileName), json);
        }

        private static string ResolveProjectRoot()
        {
            string? fromCurrent = SearchUpwards(Directory.GetCurrentDirectory(), "Config");
            if (!string.IsNullOrWhiteSpace(fromCurrent))
                return Directory.GetParent(fromCurrent)?.FullName ?? Directory.GetCurrentDirectory();

            string? fromBase = SearchUpwards(AppContext.BaseDirectory, "Config");
            if (!string.IsNullOrWhiteSpace(fromBase))
                return Directory.GetParent(fromBase)?.FullName ?? AppContext.BaseDirectory;

            return Directory.GetCurrentDirectory();
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
