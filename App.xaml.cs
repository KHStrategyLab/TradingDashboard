using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-condition01-prevhigh-branch-sweep", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunCondition01PrevHighBranchSweepBacktest();
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-daily-base-ma200-bblower-recovery-5m", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunDailyBaseMa200BbLowerRecoveryBacktest();
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
                int exitCode = RunPrevHighFirstPullbackBreakoutBacktest(useConditionSearchGate: false);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-condition01-preday-high-first-pullback-breakout", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunPrevHighFirstPullbackBreakoutBacktest(useConditionSearchGate: true);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-condition01-preday-high-first-pullback-breakout-risk5", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunPrevHighFirstPullbackBreakoutBacktest(useConditionSearchGate: true, maxEntryToPullbackRiskRate: 5m);
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-factor-sweep", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteRangeFactorSweepBacktest();
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-preday-range-rsi2-1m-prev-high-break-prev-low-stop", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteMa200MoneyBaseBacktest(
                    FiveMinuteExitMode.OneMinutePreviousLowCloseStop,
                    FiveMinuteEntryMode.OneMinuteMa5PreviousHighBreak);
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-avgif240-1m-merge-sweep", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteAvgif240OneMinuteMergeSweepBacktest();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-five-avgif240-1m-ha-exit-sweep", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunFiveMinuteAvgif240OneMinuteHeikinAshiExitSweepBacktest();
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

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-30m-baseline-scan", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunThirtyMinuteBaselineScan();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            if (e.Args.Any(arg => string.Equals(arg, "--backtest-30m-baseline-chart-smoke", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = RunThirtyMinuteBaselineChartSmoke();
                Shutdown(exitCode);
                Environment.Exit(exitCode);
                return;
            }

            string? baselineChartCodesArg = e.Args.FirstOrDefault(arg => arg.StartsWith("--backtest-30m-baseline-chart-codes=", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(baselineChartCodesArg))
            {
                string codes = baselineChartCodesArg.Split('=', 2)[1];
                int exitCode = RunThirtyMinuteBaselineChartCodes(codes);
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

        private static int RunCondition01PrevHighBranchSweepBacktest()
        {
            try
            {
                var results = new[]
                {
                    (
                        Branch: "AggressiveSupportEntry",
                        Entry: "Condition01 gate -> previous-high break -> first MA5/MA10 pullback support entry",
                        Defense: "Max next trading day 11:00",
                        Result: new PrevHighFirstPullbackSupportEntryBacktest().Run(
                            useConditionSearchGate: true,
                            useNextDay1100MaxExit: true)
                    ),
                    (
                        Branch: "AggressiveSupportEntry_1mPrevLowStop",
                        Entry: "Condition01 gate -> previous-high break -> first MA5/MA10 pullback support entry",
                        Defense: "1m completed close below previous 1m low, otherwise max next trading day 11:00",
                        Result: new PrevHighFirstPullbackSupportEntryBacktest().Run(
                            useConditionSearchGate: true,
                            useOneMinutePrevLowCloseStop: true,
                            useNextDay1100MaxExit: true)
                    ),
                    (
                        Branch: "StablePullbackHighBreakout",
                        Entry: "Condition01 gate -> previous-high break -> first pullback support -> pullback-high breakout entry",
                        Defense: "Max next trading day 11:00",
                        Result: new PrevHighFirstPullbackBreakoutBacktest().Run(
                            useConditionSearchGate: true,
                            useNextDay1100MaxExit: true)
                    ),
                    (
                        Branch: "StableBreakoutRisk5",
                        Entry: "Condition01 gate -> previous-high break -> first pullback support -> pullback-high breakout entry",
                        Defense: "Entry to pullback-low risk <= 5%, max next trading day 11:00",
                        Result: new PrevHighFirstPullbackBreakoutBacktest().Run(
                            useConditionSearchGate: true,
                            maxEntryToPullbackRiskRate: 5m,
                            useNextDay1100MaxExit: true)
                    ),
                    (
                        Branch: "StableBreakoutRisk5_1mPrevLowStop",
                        Entry: "Condition01 gate -> previous-high break -> first pullback support -> pullback-high breakout entry",
                        Defense: "Entry-to-pullback-low risk <= 5% + 1m previous-low close stop, otherwise max next trading day 11:00",
                        Result: new PrevHighFirstPullbackBreakoutBacktest().Run(
                            useConditionSearchGate: true,
                            maxEntryToPullbackRiskRate: 5m,
                            useOneMinutePrevLowCloseStop: true,
                            useNextDay1100MaxExit: true)
                    ),
                    (
                        Branch: "StableBreakoutRisk5_1mPrevLowStop_ReentryMa10To20High",
                        Entry: "Condition01 gate -> previous-high break -> first pullback support -> pullback-high breakout entry; after stop, track one 1m MA10-or-lower reset then 20-bar high breakout re-entry",
                        Defense: "Entry-to-pullback-low risk <= 5% + 1m previous-low close stop; one MA10 reset + 20-bar high breakout re-entry; max next trading day 11:00",
                        Result: new PrevHighFirstPullbackBreakoutBacktest().Run(
                            useConditionSearchGate: true,
                            maxEntryToPullbackRiskRate: 5m,
                            useOneMinutePrevLowCloseStop: true,
                            useNextDay1100MaxExit: true,
                            useOneMinuteMa10ResetTwentyHighBreakReentry: true)
                    ),
                    (
                        Branch: "OneMinuteTriggerStructuralDefense",
                        Entry: "Condition01 5m money base -> 1m trigger",
                        Defense: "Structural 1m MA5 / 5m base-low / 15m max-180m exit",
                        Result: new Condition01FiveMinuteMoneyBaseBacktest().Run(useCloseExit: false)
                    ),
                    (
                        Branch: "OneMinuteTriggerCloseExit",
                        Entry: "Condition01 5m money base -> 1m trigger",
                        Defense: "Same-day close exit",
                        Result: new Condition01FiveMinuteMoneyBaseBacktest().Run(useCloseExit: true)
                    )
                };

                string runId = $"condition01_prevhigh_branch_sweep_{DateTime.Now:yyyyMMddHHmmss}";
                var summaryResult = new
                {
                    RunId = runId,
                    Source = "Condition01 intraday money-flow gate. Current backtest gate includes 5m 40eok, latest 3x3m avg 30eok, previous-high break, intraday daily BB upper break, and 09:00-12:00 window. K/O rank filters are not fully replayed in this sweep.",
                    Results = results.Select(item => new
                    {
                        item.Branch,
                        item.Entry,
                        item.Defense,
                        item.Result.RunId,
                        item.Result.OutputDirectory,
                        item.Result.SignalCount,
                        item.Result.TradeCount,
                        item.Result.Summary.WinRate,
                        item.Result.Summary.AvgProfit,
                        item.Result.Summary.AvgLoss,
                        item.Result.Summary.Expectancy,
                        item.Result.Summary.MAE,
                        item.Result.Summary.MFE,
                        item.Result.Summary.AvgHoldingMinutes,
                        item.Result.Summary.ConsecutiveLosses
                    })
                };

                WriteBacktestJobSummary(summaryResult, "last_strategy_run_summary.json", $"strategy_run_summary_{runId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest condition01 prev-high branch sweep failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunFiveMinuteMa200MoneyBaseBacktest(
            FiveMinuteExitMode exitMode,
            FiveMinuteEntryMode entryMode = FiveMinuteEntryMode.SignalCandleClose,
            decimal rangeFactor = 0.5m)
        {
            try
            {
                var backtest = new FiveMinuteMa200MoneyBaseBacktest(exitMode, entryMode, rangeFactor);
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

        private static int RunFiveMinuteRangeFactorSweepBacktest()
        {
            try
            {
                decimal[] factors = [0.30m, 0.40m, 0.50m, 0.60m, 0.70m, 0.80m];
                var results = factors
                    .Select(factor =>
                    {
                        var backtest = new FiveMinuteMa200MoneyBaseBacktest(
                            FiveMinuteExitMode.HoldToNextDay1100,
                            FiveMinuteEntryMode.SignalCandleClose,
                            factor);
                        return (Factor: factor, Result: backtest.Run());
                    })
                    .ToList();

                string runId = $"five_preday_range_factor_sweep_{DateTime.Now:yyyyMMddHHmmss}";
                string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Storage", "Backtests", "Runs", runId);
                Directory.CreateDirectory(outputDirectory);

                var sb = new StringBuilder();
                sb.AppendLine("Factor,RunId,OutputDirectory,SignalCount,TradeCount,WinRate,AvgProfit,AvgLoss,Expectancy,MAE,MFE,AvgHoldingMinutes,ConsecutiveLosses");
                foreach ((decimal factor, BacktestRunResult result) in results)
                {
                    BacktestRunSummary summary = result.Summary;
                    sb.AppendLine(string.Join(",",
                    [
                        factor.ToString("0.00", CultureInfo.InvariantCulture),
                        result.RunId,
                        result.OutputDirectory,
                        result.SignalCount.ToString(CultureInfo.InvariantCulture),
                        result.TradeCount.ToString(CultureInfo.InvariantCulture),
                        summary.WinRate.ToString(CultureInfo.InvariantCulture),
                        summary.AvgProfit.ToString(CultureInfo.InvariantCulture),
                        summary.AvgLoss.ToString(CultureInfo.InvariantCulture),
                        summary.Expectancy.ToString(CultureInfo.InvariantCulture),
                        summary.MAE.ToString(CultureInfo.InvariantCulture),
                        summary.MFE.ToString(CultureInfo.InvariantCulture),
                        summary.AvgHoldingMinutes.ToString(CultureInfo.InvariantCulture),
                        summary.ConsecutiveLosses.ToString(CultureInfo.InvariantCulture)
                    ]));
                }

                string summaryPath = Path.Combine(outputDirectory, "range_factor_sweep_summary.csv");
                File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);
                SaveRangeFactorFailureReview(outputDirectory, results);

                var summaryResult = new
                {
                    RunId = runId,
                    OutputDirectory = outputDirectory,
                    SummaryPath = summaryPath,
                    Factors = results.Select(item => new
                    {
                        Factor = item.Factor,
                        item.Result.RunId,
                        item.Result.SignalCount,
                        item.Result.TradeCount,
                        item.Result.Summary.WinRate,
                        item.Result.Summary.Expectancy,
                        item.Result.Summary.MAE,
                        item.Result.Summary.MFE
                    })
                };
                WriteBacktestJobSummary(summaryResult, "last_strategy_run_summary.json", $"strategy_run_summary_{runId}.json");
                Console.WriteLine($"factor sweep saved: {summaryPath}");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest 5m range-factor sweep failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunFiveMinuteAvgif240OneMinuteMergeSweepBacktest()
        {
            try
            {
                FiveMinuteEntryMode[] modes =
                [
                    FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighLight,
                    FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighNormal,
                    FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighStrong
                ];

                var results = modes
                    .Select(mode =>
                    {
                        var backtest = new FiveMinuteMa200MoneyBaseBacktest(
                            FiveMinuteExitMode.AvgifUpperOrOneMinutePreviousLowCloseStop,
                            mode);
                        return (Mode: mode, Result: backtest.Run());
                    })
                    .ToList();

                string runId = $"avgif240_1m_merge_sweep_{DateTime.Now:yyyyMMddHHmmss}";
                var summaryResult = new
                {
                    RunId = runId,
                    Results = results.Select(item => new
                    {
                        VolumeFilter = item.Mode.ToString().Replace("AvgifLowerOneMinutePrevHigh", string.Empty, StringComparison.Ordinal),
                        item.Result.RunId,
                        item.Result.OutputDirectory,
                        item.Result.SignalCount,
                        item.Result.TradeCount,
                        item.Result.Summary.WinRate,
                        item.Result.Summary.AvgProfit,
                        item.Result.Summary.AvgLoss,
                        item.Result.Summary.Expectancy,
                        item.Result.Summary.MAE,
                        item.Result.Summary.MFE,
                        item.Result.Summary.AvgHoldingMinutes,
                        item.Result.Summary.ConsecutiveLosses
                    })
                };

                WriteBacktestJobSummary(summaryResult, "last_strategy_run_summary.json", $"strategy_run_summary_{runId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest AVGIF240 1m merge sweep failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static int RunFiveMinuteAvgif240OneMinuteHeikinAshiExitSweepBacktest()
        {
            try
            {
                FiveMinuteExitMode[] modes =
                [
                    FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamage,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurn,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndPrevLowDamage,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearHold2,
                    FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamageOrBearHold2,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndMa5Damage,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndVolumeWeak
                ];

                var results = modes
                    .Select(mode =>
                    {
                        var backtest = new FiveMinuteMa200MoneyBaseBacktest(
                            mode,
                            FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighNormal);
                        return (Mode: mode, Result: backtest.Run());
                    })
                    .ToList();

                string runId = $"avgif240_1m_ha_exit_sweep_{DateTime.Now:yyyyMMddHHmmss}";
                var summaryResult = new
                {
                    RunId = runId,
                    Entry = "AVGIF240 lower recovery + 1m MA5 previous-high break + normal volume",
                    Results = results.Select(item => new
                    {
                        ExitMode = item.Mode.ToString().Replace("OneMinuteHeikinAshi", "HA_", StringComparison.Ordinal),
                        item.Result.RunId,
                        item.Result.OutputDirectory,
                        item.Result.SignalCount,
                        item.Result.TradeCount,
                        item.Result.Summary.WinRate,
                        item.Result.Summary.AvgProfit,
                        item.Result.Summary.AvgLoss,
                        item.Result.Summary.Expectancy,
                        item.Result.Summary.MAE,
                        item.Result.Summary.MFE,
                        item.Result.Summary.AvgHoldingMinutes,
                        item.Result.Summary.ConsecutiveLosses
                    })
                };

                WriteBacktestJobSummary(summaryResult, "last_strategy_run_summary.json", $"strategy_run_summary_{runId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest AVGIF240 1m HA exit sweep failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 1;
            }
        }

        private static void SaveRangeFactorFailureReview(
            string outputDirectory,
            IReadOnlyList<(decimal Factor, BacktestRunResult Result)> results)
        {
            List<RangeFactorDiagnosticCsvRow> rows = [];
            foreach ((decimal factor, BacktestRunResult result) in results)
            {
                string path = Path.Combine(result.OutputDirectory, "range_factor_trade_diagnostics.csv");
                if (!File.Exists(path))
                    continue;

                rows.AddRange(ReadRangeFactorDiagnostics(path, factor));
            }

            if (rows.Count == 0)
                return;

            SaveRangeFactorBucketSummary(outputDirectory, rows);
            SaveRangeFactorCautionRows(outputDirectory, rows);
            SaveRangeFactorNoBuyReview(outputDirectory, rows);
        }

        private static List<RangeFactorDiagnosticCsvRow> ReadRangeFactorDiagnostics(string path, decimal fallbackFactor)
        {
            List<string> lines = [.. File.ReadLines(path, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line))];
            if (lines.Count < 2)
                return [];

            string[] headers = ParseCsvLine(lines[0]);
            Dictionary<string, int> indexes = headers
                .Select((header, index) => (header, index))
                .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);

            List<RangeFactorDiagnosticCsvRow> rows = [];
            foreach (string line in lines.Skip(1))
            {
                string[] fields = ParseCsvLine(line);
                string Get(string name) =>
                    indexes.TryGetValue(name, out int index) && index >= 0 && index < fields.Length
                        ? fields[index]
                        : string.Empty;

                decimal ReadDecimal(string name)
                {
                    string value = Get(name);
                    return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed)
                        ? parsed
                        : 0m;
                }

                long ReadLong(string name)
                {
                    string value = Get(name);
                    return long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsed)
                        ? parsed
                        : 0L;
                }

                decimal factor = ReadDecimal("Factor");
                if (factor <= 0)
                    factor = fallbackFactor;

                rows.Add(new RangeFactorDiagnosticCsvRow(
                    factor,
                    Get("Code"),
                    Get("Market"),
                    Get("SignalDate"),
                    Get("SignalHour"),
                    Get("BaseTime"),
                    Get("EntryTime"),
                    Get("ExitTime"),
                    ReadLong("DayOpen"),
                    ReadLong("PreviousHigh"),
                    ReadLong("PreviousLow"),
                    ReadDecimal("TriggerLine"),
                    ReadDecimal("Rsi2"),
                    ReadDecimal("PriceMa5"),
                    ReadDecimal("PriceMa20"),
                    ReadDecimal("PriceMa60"),
                    ReadDecimal("Ma200"),
                    ReadDecimal("Ma200GapRate"),
                    ReadLong("Volume"),
                    ReadDecimal("VolumeMa5"),
                    ReadDecimal("VolumeMa20"),
                    ReadDecimal("VolumeMa60"),
                    ReadDecimal("TradingValueMa5"),
                    ReadDecimal("TradingValueMa20"),
                    ReadDecimal("TradingValueMa60"),
                    ReadDecimal("BodyRate"),
                    ReadDecimal("UpperWickRate"),
                    ReadDecimal("LowerWickRate"),
                    ReadDecimal("CloseLocationRate"),
                    ReadDecimal("DailyMa5"),
                    ReadDecimal("DailyMa20"),
                    ReadDecimal("DailyMa60"),
                    ReadDecimal("DailyMa5To60Rate"),
                    ReadLong("DailyTradingValue"),
                    ReadLong("BaseTradingValue"),
                    ReadDecimal("TriggerToPreviousHighRate"),
                    Get("DailyValueBucket"),
                    Get("Ma200PositionBucket"),
                    Get("VolumeExpansionBucket"),
                    Get("TradingValueExpansionBucket"),
                    Get("CandleStructureBucket"),
                    Get("PriceMaStructureBucket"),
                    Get("DailyMaSpreadBucket"),
                    ReadDecimal("ProfitRate"),
                    ReadDecimal("MAE"),
                    ReadDecimal("MFE"),
                    Get("OutcomeBucket"),
                    Get("ExitReason")));
            }

            return rows;
        }

        private static void SaveRangeFactorBucketSummary(string outputDirectory, IReadOnlyList<RangeFactorDiagnosticCsvRow> rows)
        {
            List<RangeFactorBucketSummaryRow> summaries = [];
            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "DailyValue", Bucket = row.DailyValueBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "Ma200Position", Bucket = row.Ma200PositionBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "VolumeExpansion", Bucket = row.VolumeExpansionBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "TradingValueExpansion", Bucket = row.TradingValueExpansionBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "CandleStructure", Bucket = row.CandleStructureBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "PriceMaStructure", Bucket = row.PriceMaStructureBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "DailyMaSpread", Bucket = row.DailyMaSpreadBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            StringBuilder builder = new();
            AppendCsvLine(builder, "Factor", "BucketType", "Bucket", "TradeCount", "FailureCount", "CautionCount", "FailureRate", "CautionRate", "AvgProfit", "AvgMAE", "AvgMFE");
            foreach (RangeFactorBucketSummaryRow row in summaries
                .OrderBy(item => item.Factor)
                .ThenBy(item => item.BucketType, StringComparer.Ordinal)
                .ThenBy(item => item.Bucket, StringComparer.Ordinal))
            {
                AppendCsvLine(
                    builder,
                    row.Factor.ToString("0.00", CultureInfo.InvariantCulture),
                    row.BucketType,
                    row.Bucket,
                    row.TradeCount.ToString(CultureInfo.InvariantCulture),
                    row.FailureCount.ToString(CultureInfo.InvariantCulture),
                    row.CautionCount.ToString(CultureInfo.InvariantCulture),
                    row.FailureRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.CautionRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.AvgProfit.ToString("0.####", CultureInfo.InvariantCulture),
                    row.AvgMAE.ToString("0.####", CultureInfo.InvariantCulture),
                    row.AvgMFE.ToString("0.####", CultureInfo.InvariantCulture));
            }

            File.WriteAllText(Path.Combine(outputDirectory, "range_factor_bucket_summary.csv"), builder.ToString(), Encoding.UTF8);
        }

        private static RangeFactorBucketSummaryRow BuildRangeFactorBucketSummary(
            decimal factor,
            string bucketType,
            string bucket,
            IEnumerable<RangeFactorDiagnosticCsvRow> source)
        {
            List<RangeFactorDiagnosticCsvRow> rows = [.. source];
            int failureCount = rows.Count(IsFailedOutcome);
            int cautionCount = rows.Count(IsCautionOutcome);
            return new RangeFactorBucketSummaryRow(
                factor,
                bucketType,
                bucket,
                rows.Count,
                failureCount,
                cautionCount,
                rows.Count > 0 ? failureCount / (decimal)rows.Count * 100m : 0m,
                rows.Count > 0 ? cautionCount / (decimal)rows.Count * 100m : 0m,
                rows.Count > 0 ? rows.Average(row => row.ProfitRate) : 0m,
                rows.Count > 0 ? rows.Average(row => row.Mae) : 0m,
                rows.Count > 0 ? rows.Average(row => row.Mfe) : 0m);
        }

        private static void SaveRangeFactorCautionRows(string outputDirectory, IReadOnlyList<RangeFactorDiagnosticCsvRow> rows)
        {
            List<RangeFactorDiagnosticCsvRow> cautionRows = [.. rows
                .Where(IsCautionOutcome)
                .OrderBy(row => row.Factor)
                .ThenBy(row => row.Mae)
                .ThenBy(row => row.SignalDate, StringComparer.Ordinal)
                .ThenBy(row => row.Code, StringComparer.Ordinal)];

            StringBuilder builder = new();
            AppendCsvLine(
                builder,
                "Factor",
                "Code",
                "Market",
                "SignalDate",
                "SignalHour",
                "DailyValueBucket",
                "Ma200PositionBucket",
                "VolumeExpansionBucket",
                "TradingValueExpansionBucket",
                "CandleStructureBucket",
                "PriceMaStructureBucket",
                "DailyMaSpreadBucket",
                "ProfitRate",
                "MAE",
                "MFE",
                "OutcomeBucket",
                "NoBuyCandidateReason",
                "ExitReason");

            foreach (RangeFactorDiagnosticCsvRow row in cautionRows)
            {
                AppendCsvLine(
                    builder,
                    row.Factor.ToString("0.00", CultureInfo.InvariantCulture),
                    row.Code,
                    row.Market,
                    row.SignalDate,
                    row.SignalHour,
                    row.DailyValueBucket,
                    row.Ma200PositionBucket,
                    row.VolumeExpansionBucket,
                    row.TradingValueExpansionBucket,
                    row.CandleStructureBucket,
                    row.PriceMaStructureBucket,
                    row.DailyMaSpreadBucket,
                    row.ProfitRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.Mae.ToString("0.####", CultureInfo.InvariantCulture),
                    row.Mfe.ToString("0.####", CultureInfo.InvariantCulture),
                    row.OutcomeBucket,
                    BuildNoBuyCandidateReason(row),
                    row.ExitReason);
            }

            File.WriteAllText(Path.Combine(outputDirectory, "range_factor_caution_rows.csv"), builder.ToString(), Encoding.UTF8);
        }

        private static void SaveRangeFactorNoBuyReview(string outputDirectory, IReadOnlyList<RangeFactorDiagnosticCsvRow> rows)
        {
            List<RangeFactorBucketSummaryRow> summaries = [];
            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "DailyValue", Bucket = row.DailyValueBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "VolumeExpansion", Bucket = row.VolumeExpansionBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "TradingValueExpansion", Bucket = row.TradingValueExpansionBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "CandleStructure", Bucket = row.CandleStructureBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            foreach (var group in rows.GroupBy(row => new { row.Factor, BucketType = "PriceMaStructure", Bucket = row.PriceMaStructureBucket }))
                summaries.Add(BuildRangeFactorBucketSummary(group.Key.Factor, group.Key.BucketType, group.Key.Bucket, group));

            List<RangeFactorBucketSummaryRow> cautionBuckets = [.. summaries
                .Where(row => row.TradeCount >= 3)
                .OrderByDescending(row => row.CautionRate)
                .ThenBy(row => row.AvgProfit)
                .Take(12)];

            StringBuilder builder = new();
            builder.AppendLine("# Range Factor NoBuy 후보 리뷰");
            builder.AppendLine();
            builder.AppendLine("이 보고서는 수익을 따라가기보다 실패/흔들림 구간에서 안 살 이유 후보를 찾기 위한 전처리 결과다.");
            builder.AppendLine();
            builder.AppendLine("## 주의 후보 버킷");
            builder.AppendLine();
            builder.AppendLine("| Factor | BucketType | Bucket | Trades | Failure% | Caution% | AvgProfit | AvgMAE | AvgMFE |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (RangeFactorBucketSummaryRow row in cautionBuckets)
            {
                builder.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {row.Factor:0.00} | {row.BucketType} | {row.Bucket} | {row.TradeCount} | {row.FailureRate:0.##}% | {row.CautionRate:0.##}% | {row.AvgProfit:0.##}% | {row.AvgMAE:0.##}% | {row.AvgMFE:0.##}% |"));
            }

            builder.AppendLine();
            builder.AppendLine("## 현재 해석");
            builder.AppendLine();
            builder.AppendLine("- 일봉 거래대금, 5분 MA200 위치, 일봉 이평 정렬만으로는 부족하다.");
            builder.AppendLine("- 이번 버전부터 거래량 이평, 거래대금 이평, 봉 구조, 가격 이평 구조를 같이 본다.");
            builder.AppendLine("- `vol-weak`, `value-weak`, `bear`, `bear-close-low`, `bull-upper-wick-heavy`는 매수 보류 후보로 우선 복기한다.");
            builder.AppendLine("- 다음 검토는 `caution rows`를 차트로 열어 실제로 거래가 붙었는데 밀린 자리인지, 거래가 죽은 눌림인지 확인한다.");
            builder.AppendLine();
            builder.AppendLine("## 산출물");
            builder.AppendLine();
            builder.AppendLine("- `range_factor_bucket_summary.csv`: 조건 축별 실패율/주의율");
            builder.AppendLine("- `range_factor_caution_rows.csv`: 실패 또는 큰 흔들림 후보 행");

            File.WriteAllText(Path.Combine(outputDirectory, "range_factor_no_buy_review.md"), builder.ToString(), Encoding.UTF8);
        }
        private static bool IsFailedOutcome(RangeFactorDiagnosticCsvRow row) =>
            string.Equals(row.OutcomeBucket, "failed", StringComparison.OrdinalIgnoreCase);

        private static bool IsCautionOutcome(RangeFactorDiagnosticCsvRow row) =>
            IsFailedOutcome(row) || row.Mae <= -10m;

        private static string BuildNoBuyCandidateReason(RangeFactorDiagnosticCsvRow row)
        {
            List<string> reasons = [];
            if (string.Equals(row.DailyValueBucket, "under500eok", StringComparison.Ordinal))
                reasons.Add("daily value under 500eok");
            if (row.Mae <= -10m)
                reasons.Add("MAE <= -10%");
            if (string.Equals(row.Ma200PositionBucket, "below-ma200", StringComparison.Ordinal))
                reasons.Add("below 5m MA200");
            if (string.Equals(row.Ma200PositionBucket, "ma200-none-or-touch", StringComparison.Ordinal))
                reasons.Add("MA200 unavailable/touch ambiguous");
            if (string.Equals(row.VolumeExpansionBucket, "vol-weak", StringComparison.Ordinal))
                reasons.Add("volume below short average");
            if (string.Equals(row.TradingValueExpansionBucket, "value-weak", StringComparison.Ordinal))
                reasons.Add("trading value below short average");
            if (string.Equals(row.CandleStructureBucket, "bear", StringComparison.Ordinal) ||
                string.Equals(row.CandleStructureBucket, "bear-close-low", StringComparison.Ordinal))
                reasons.Add("bearish signal candle");
            if (string.Equals(row.CandleStructureBucket, "bull-upper-wick-heavy", StringComparison.Ordinal))
                reasons.Add("upper wick heavier than body");
            if (string.Equals(row.PriceMaStructureBucket, "price-below-ma60-short-down", StringComparison.Ordinal) ||
                string.Equals(row.PriceMaStructureBucket, "price-below-ma60", StringComparison.Ordinal))
                reasons.Add("price below 5m MA60");
            if (string.Equals(row.DailyMaSpreadBucket, "daily-ma-not-aligned", StringComparison.Ordinal))
                reasons.Add("daily MA not aligned");
            if (row.Mfe < 3m)
                reasons.Add("MFE < 3%");

            return reasons.Count > 0 ? string.Join(" / ", reasons) : "review chart context";
        }

        private static string[] ParseCsvLine(string line)
        {
            List<string> fields = [];
            StringBuilder field = new();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char current = line[i];
                if (current == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (current == ',' && !quoted)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(current);
                }
            }

            fields.Add(field.ToString());
            return [.. fields];
        }

        private static void AppendCsvLine(StringBuilder builder, params string[] values)
        {
            builder.AppendLine(string.Join(",", values.Select(EscapeCsv)));
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
                return value;

            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        private sealed record RangeFactorDiagnosticCsvRow(
            decimal Factor,
            string Code,
            string Market,
            string SignalDate,
            string SignalHour,
            string BaseTime,
            string EntryTime,
            string ExitTime,
            long DayOpen,
            long PreviousHigh,
            long PreviousLow,
            decimal TriggerLine,
            decimal Rsi2,
            decimal PriceMa5,
            decimal PriceMa20,
            decimal PriceMa60,
            decimal Ma200,
            decimal Ma200GapRate,
            long Volume,
            decimal VolumeMa5,
            decimal VolumeMa20,
            decimal VolumeMa60,
            decimal TradingValueMa5,
            decimal TradingValueMa20,
            decimal TradingValueMa60,
            decimal BodyRate,
            decimal UpperWickRate,
            decimal LowerWickRate,
            decimal CloseLocationRate,
            decimal DailyMa5,
            decimal DailyMa20,
            decimal DailyMa60,
            decimal DailyMa5To60Rate,
            long DailyTradingValue,
            long BaseTradingValue,
            decimal TriggerToPreviousHighRate,
            string DailyValueBucket,
            string Ma200PositionBucket,
            string VolumeExpansionBucket,
            string TradingValueExpansionBucket,
            string CandleStructureBucket,
            string PriceMaStructureBucket,
            string DailyMaSpreadBucket,
            decimal ProfitRate,
            decimal Mae,
            decimal Mfe,
            string OutcomeBucket,
            string ExitReason);

        private sealed record RangeFactorBucketSummaryRow(
            decimal Factor,
            string BucketType,
            string Bucket,
            int TradeCount,
            int FailureCount,
            int CautionCount,
            decimal FailureRate,
            decimal CautionRate,
            decimal AvgProfit,
            decimal AvgMAE,
            decimal AvgMFE);

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

        private static int RunDailyBaseMa200BbLowerRecoveryBacktest()
        {
            try
            {
                var backtest = new DailyBaseMa200BbLowerRecoveryBacktest();
                BacktestRunResult result = backtest.Run();
                WriteBacktestJobSummary(result, "last_strategy_run_summary.json", $"strategy_run_summary_{result.RunId}.json");
                return 0;
            }
            catch (Exception ex)
            {
                var result = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"backtest daily-base MA200 BBLower recovery failed: {ex.GetType().Name}: {ex.Message}"
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

        private static int RunPrevHighFirstPullbackBreakoutBacktest(bool useConditionSearchGate, decimal? maxEntryToPullbackRiskRate = null)
        {
            try
            {
                var backtest = new PrevHighFirstPullbackBreakoutBacktest();
                BacktestRunResult result = backtest.Run(useConditionSearchGate, maxEntryToPullbackRiskRate);
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

        private static int RunThirtyMinuteBaselineScan()
        {
            try
            {
                var service = new ThirtyMinuteBaselineService();
                string outputDirectory = service.SaveFullScan(lookbackBars: 600);
                var summary = new
                {
                    RunId = Path.GetFileName(outputDirectory),
                    OutputDirectory = outputDirectory,
                    Csv = Path.Combine(outputDirectory, "thirty_minute_baselines.csv"),
                    Message = "30m 600-bar baseline scan completed"
                };
                WriteBacktestJobSummary(summary, "last_30m_baseline_scan_summary.json", $"30m_baseline_scan_summary_{summary.RunId}.json");
                Console.WriteLine($"30m baseline scan saved: {outputDirectory}");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"30m baseline scan failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(summary, "last_30m_baseline_scan_summary.json", $"30m_baseline_scan_summary_{summary.RunId}.json");
                return 1;
            }
        }

        private static int RunThirtyMinuteBaselineChartSmoke()
        {
            try
            {
                var service = new ThirtyMinuteBaselineService();
                string outputDirectory = service.SaveCandidateChartSmoke(lookbackBars: 600, chartBars: 260, maxCharts: 12);
                var summary = new
                {
                    RunId = Path.GetFileName(outputDirectory),
                    OutputDirectory = outputDirectory,
                    Csv = Path.Combine(outputDirectory, "chart_smoke_summary.csv"),
                    Message = "30m 600-bar baseline chart smoke completed"
                };
                WriteBacktestJobSummary(summary, "last_30m_baseline_chart_smoke_summary.json", $"30m_baseline_chart_smoke_summary_{summary.RunId}.json");
                Console.WriteLine($"30m baseline chart smoke saved: {outputDirectory}");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"30m baseline chart smoke failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(summary, "last_30m_baseline_chart_smoke_summary.json", $"30m_baseline_chart_smoke_summary_{summary.RunId}.json");
                return 1;
            }
        }

        private static int RunThirtyMinuteBaselineChartCodes(string codes)
        {
            try
            {
                var service = new ThirtyMinuteBaselineService();
                string[] codeList = [.. (codes ?? string.Empty)
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
                string outputDirectory = service.SaveCodeChartSmoke(codeList, lookbackBars: 600, chartBars: 260);
                var summary = new
                {
                    RunId = Path.GetFileName(outputDirectory),
                    Codes = codeList,
                    OutputDirectory = outputDirectory,
                    Csv = Path.Combine(outputDirectory, "chart_code_summary.csv"),
                    Message = "30m 600-bar baseline code chart completed"
                };
                WriteBacktestJobSummary(summary, "last_30m_baseline_chart_codes_summary.json", $"30m_baseline_chart_codes_summary_{summary.RunId}.json");
                Console.WriteLine($"30m baseline code chart saved: {outputDirectory}");
                return 0;
            }
            catch (Exception ex)
            {
                var summary = new
                {
                    RunId = DateTime.Now.ToString("yyyyMMddHHmmss"),
                    Error = $"30m baseline code chart failed: {ex.GetType().Name}: {ex.Message}"
                };
                WriteBacktestJobSummary(summary, "last_30m_baseline_chart_codes_summary.json", $"30m_baseline_chart_codes_summary_{summary.RunId}.json");
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
