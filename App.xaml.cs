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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-daily-datastore-al-all", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunBacktestDailyDataStoreJobAsync(new BacktestSettings
                {
                    MarketMode = "AL_ONLY"
                }).ConfigureAwait(true);
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-minute-datastore-al-smoke", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunBacktestMinuteDataStoreJobAsync(new BacktestSettings
                {
                    MarketMode = "AL_ONLY",
                    MinuteCodeFilter = "000810",
                    MinuteMarketFilter = "AL",
                    MaxMinuteStockMarketGroups = 1,
                    MinuteIntervals = [5],
                    MinuteFetchCount = 1200
                }).ConfigureAwait(true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-minute-datastore-al-all", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunBacktestMinuteDataStoreJobAsync(new BacktestSettings
                {
                    MarketMode = "AL_ONLY",
                    MinuteMarketFilter = "AL",
                    MinuteIntervals = [1, 5, 15],
                    MinuteFetchCount = 1200
                }).ConfigureAwait(true);
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-ten-pullback-five-breakout-signal-exit", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunTenPullbackFiveBreakoutSignalExitBacktest();
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-center-5m-1m-signal-exit", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseCenterPullbackBacktest(
                    baseMinute: 5,
                    entryMinute: 1,
                    useOneMinuteTrigger: true,
                    baseRisePercent: 0.8m,
                    baseTradingValueWon: 500_000_000,
                    useSignalExit: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-ma60-recover-5m-1m-draft", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseCenterPullbackBacktest(
                    baseMinute: 5,
                    entryMinute: 1,
                    useOneMinuteTrigger: true,
                    baseRisePercent: 0.8m,
                    baseRiseMaxPercent: 2.0m,
                    baseTradingValueWon: 300_000_000,
                    useSignalExit: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-ma60-recover-5m-1m-draft-top100", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseCenterPullbackBacktest(
                    baseMinute: 5,
                    entryMinute: 1,
                    useOneMinuteTrigger: true,
                    baseRisePercent: 0.8m,
                    baseRiseMaxPercent: 2.0m,
                    baseTradingValueWon: 300_000_000,
                    useSignalExit: true,
                    maxIntradayTradingValueRank: 100);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-condition01-5m-money-1m-trigger", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunCondition01FiveMinuteMoneyBaseBacktest(useCloseExit: false);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-condition01-5m-money-1m-close-exit", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunCondition01FiveMinuteMoneyBaseBacktest(useCloseExit: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-ma200-bblower-rebound", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunMa200BbLowerReboundBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-ma240-lower-deviation-rebound", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunMa240LowerDeviationReboundBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-preday-high-first-pullback-breakout", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunPrevHighFirstPullbackBreakoutBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-preday-high-first-pullback-support-entry", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunPrevHighFirstPullbackSupportEntryBacktest(useConditionSearchGate: false);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-condition01-preday-high-first-pullback-support-entry", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunPrevHighFirstPullbackSupportEntryBacktest(useConditionSearchGate: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-preday-high-pullback-onebar-rsi2", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunPrevHighPullbackOneBarRsiBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-ma200-money-1m-trigger", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(FiveMinuteExitMode.HoldToNextDay1100);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-linear-exit", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(FiveMinuteExitMode.LinearRegressionCrossDown);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-ma240-upper-exit", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(FiveMinuteExitMode.Ma240PositiveDeviationUpperCrossUp);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-signal-low-5m-close-stop", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(FiveMinuteExitMode.SignalLowFiveMinuteCloseStop);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-ma200-5m-close-stop", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(FiveMinuteExitMode.Ma200FiveMinuteCloseStop);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-1m-ma-touch-signal-low-stop", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(
                    FiveMinuteExitMode.SignalLowFiveMinuteCloseStop,
                    FiveMinuteEntryMode.OneMinuteMa5CrossMa60TouchMa60);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-1m-ma-touch-ma200-5m-close-stop", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(
                    FiveMinuteExitMode.Ma200FiveMinuteCloseStop,
                    FiveMinuteEntryMode.OneMinuteMa5CrossMa60TouchMa60);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-1m-ma20-touch-ma200-5m-close-stop", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(
                    FiveMinuteExitMode.Ma200FiveMinuteCloseStop,
                    FiveMinuteEntryMode.OneMinuteMa5CrossMa20TouchMa20);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-daily-500eok-20p-10m-ma60-recover", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunDailyBaseTenMinuteMa60RecoverBacktest(10);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-daily-500eok-20p-15m-ma60-recover", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunDailyBaseTenMinuteMa60RecoverBacktest(15);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-small-base-stoch-ab", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseStochasticAbBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-stoch-quick-reaction", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunSmallBaseStochasticQuickReactionBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--leader-history-rebuild", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunLeaderHistoryRebuild();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--leader-history-promote-candidates", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunLeaderHistoryCandidatePromotion();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--candidate-ledger-rebuild", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunCandidateLedgerRebuild();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--candidate-ledger-enrich-fundamentals", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunCandidateLedgerFundamentalEnrichAsync().ConfigureAwait(true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            base.OnStartup(e);
        }

        private static async Task<int> RunBacktestDailyDataStoreJobAsync(BacktestSettings? overrideSettings = null)
        {
            try
            {
                AppConfig config = LocalSettingsLoader.Load();
                var kiwoomService = new KiwoomRestConditionService(config.Kiwoom);
                var job = new BacktestDailyDataStoreJob(kiwoomService, overrideSettings ?? config.Backtest);
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

        private static async Task<int> RunBacktestMinuteDataStoreJobAsync(BacktestSettings? overrideSettings = null)
        {
            try
            {
                AppConfig config = LocalSettingsLoader.Load();
                var kiwoomService = new KiwoomRestConditionService(config.Kiwoom);
                var job = new BacktestMinuteDataStoreJob(kiwoomService, overrideSettings ?? config.Backtest);
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

        private static int RunTenPullbackFiveBreakoutSignalExitBacktest()
        {
            try
            {
                var backtest = new TenMinutePullbackFiveMinuteBreakoutSignalExitBacktest();
                BacktestRunResult result = backtest.Run(maxHoldingMinutes: 180);
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest signal-exit strategy failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunSmallBaseCenterPullbackBacktest(
            int baseMinute,
            int entryMinute,
            bool useOneMinuteTrigger,
            decimal baseRisePercent = 1.0m,
            decimal? baseRiseMaxPercent = null,
            long baseTradingValueWon = 1_000_000_000,
            bool useSignalExit = false,
            int? maxIntradayTradingValueRank = null)
        {
            try
            {
                var backtest = new SmallBaseCenterPullbackBacktest();
                BacktestRunResult result = backtest.Run(
                    baseMinute: baseMinute,
                    entryMinute: entryMinute,
                    triggerMinute: useOneMinuteTrigger ? 1 : 0,
                    observationMinutes: 180,
                    baseRisePercent: baseRisePercent,
                    baseRiseMaxPercent: baseRiseMaxPercent,
                    baseTradingValueWon: baseTradingValueWon,
                    useSignalExit: useSignalExit,
                    maxIntradayTradingValueRank: maxIntradayTradingValueRank);
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

        private static int RunCondition01FiveMinuteMoneyBaseBacktest(bool useCloseExit)
        {
            try
            {
                var backtest = new Condition01FiveMinuteMoneyBaseBacktest();
                BacktestRunResult result = backtest.Run(useCloseExit);
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest condition01 5m money-base failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunFiveMinuteMa200MoneyBaseBacktest(
            FiveMinuteExitMode exitMode,
            FiveMinuteEntryMode entryMode = FiveMinuteEntryMode.SignalCandleClose)
        {
            try
            {
                var backtest = new FiveMinuteMa200MoneyBaseBacktest(exitMode, entryMode);
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest 5m MA200 money-base failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunMa200BbLowerReboundBacktest()
        {
            try
            {
                var backtest = new Ma200BbLowerReboundBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest MA200 BBLower rebound failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunMa240LowerDeviationReboundBacktest()
        {
            try
            {
                var backtest = new Ma240LowerDeviationReboundBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest MA240 lower-deviation rebound failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunPrevHighFirstPullbackBreakoutBacktest()
        {
            try
            {
                var backtest = new PrevHighFirstPullbackBreakoutBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest prev-high first-pullback breakout failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunPrevHighFirstPullbackSupportEntryBacktest(bool useConditionSearchGate)
        {
            try
            {
                var backtest = new PrevHighFirstPullbackSupportEntryBacktest();
                BacktestRunResult result = backtest.Run(useConditionSearchGate);
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest prev-high first-pullback support entry failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunPrevHighPullbackOneBarRsiBacktest()
        {
            try
            {
                var backtest = new PrevHighPullbackOneBarRsiBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest prev-high one-bar RSI2 failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunDailyBaseTenMinuteMa60RecoverBacktest(int minuteInterval)
        {
            try
            {
                var backtest = new DailyBaseTenMinuteMa60RecoverBacktest(minuteInterval);
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest daily 500eok 20p {minuteInterval}m MA60 recover failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunSmallBaseStochasticAbBacktest()
        {
            try
            {
                var backtest = new SmallBaseStochasticAbBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest small-base stochastic A/B failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunSmallBaseStochasticQuickReactionBacktest()
        {
            try
            {
                var backtest = new SmallBaseStochasticQuickReactionBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest stochastic quick reaction failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunLeaderHistoryRebuild()
        {
            try
            {
                var job = new LeaderHistoryRebuildJob();
                LeaderHistoryRebuildSummary summary = job.Rebuild(lookbackTradingDays: 6);
                WriteBacktestJobSummary(summary, "last_leader_history_rebuild_summary.json", $"leader_history_rebuild_summary_{summary.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new LeaderHistoryRebuildSummary
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Logs = [$"leader history rebuild failed: {ex.GetType().Name}: {ex.Message}"]
                };
                WriteBacktestJobSummary(summary, "last_leader_history_rebuild_summary.json", $"leader_history_rebuild_summary_{summary.RunId}.json");
                return 1;
            }
        }

        private static int RunLeaderHistoryCandidatePromotion()
        {
            try
            {
                var job = new LeaderHistoryCandidatePromotionJob();
                LeaderHistoryRebuildSummary summary = job.Promote(lookbackTradingDays: 6);
                WriteBacktestJobSummary(summary, "last_leader_history_promotion_summary.json", $"leader_history_promotion_summary_{summary.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new LeaderHistoryRebuildSummary
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Logs = [$"leader history candidate promotion failed: {ex.GetType().Name}: {ex.Message}"]
                };
                WriteBacktestJobSummary(summary, "last_leader_history_promotion_summary.json", $"leader_history_promotion_summary_{summary.RunId}.json");
                return 1;
            }
        }

        private static int RunCandidateLedgerRebuild()
        {
            try
            {
                var job = new CandidateLedgerRebuildJob();
                CandidateLedgerRebuildSummary summary = job.Rebuild(lookbackTradingDays: 6);
                WriteBacktestJobSummary(summary, "last_candidate_ledger_rebuild_summary.json", $"candidate_ledger_rebuild_summary_{summary.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new CandidateLedgerRebuildSummary
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Logs = [$"candidate ledger rebuild failed: {ex.GetType().Name}: {ex.Message}"]
                };
                WriteBacktestJobSummary(summary, "last_candidate_ledger_rebuild_summary.json", $"candidate_ledger_rebuild_summary_{summary.RunId}.json");
                return 1;
            }
        }

        private static async Task<int> RunCandidateLedgerFundamentalEnrichAsync()
        {
            try
            {
                AppConfig config = LocalSettingsLoader.Load();
                var kiwoomService = new KiwoomRestConditionService(config.Kiwoom);
                var job = new CandidateLedgerFundamentalEnrichJob(kiwoomService);
                CandidateLedgerEnrichSummary summary = await job.EnrichAsync().ConfigureAwait(false);
                WriteBacktestJobSummary(summary, "last_candidate_ledger_enrich_summary.json", $"candidate_ledger_enrich_summary_{summary.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new CandidateLedgerEnrichSummary
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Logs = [$"candidate ledger fundamental enrich failed: {ex.GetType().Name}: {ex.Message}"]
                };
                WriteBacktestJobSummary(summary, "last_candidate_ledger_enrich_summary.json", $"candidate_ledger_enrich_summary_{summary.RunId}.json");
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
