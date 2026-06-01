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

        private static void WriteBacktestJobSummary(BacktestDatasetUpdateSummary summary)
        {
            string root = ResolveProjectRoot();
            string directory = Path.Combine(root, "Storage", "Backtests", "DataStore", "metadata");
            Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(directory, "last_daily_update_summary.json"), json);
            File.WriteAllText(Path.Combine(directory, $"daily_update_summary_{summary.RunId}.json"), json);
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
