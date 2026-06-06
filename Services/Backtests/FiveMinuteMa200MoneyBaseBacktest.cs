using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Backtests
{
    public enum FiveMinuteExitMode
    {
        HoldToNextDay1100,
        LinearRegressionCrossDown,
        Ma240PositiveDeviationUpperCrossUp,
        SignalLowFiveMinuteCloseStop,
        Ma200FiveMinuteCloseStop,
        OneMinutePreviousLowCloseStop,
        AvgifUpperOrOneMinutePreviousLowCloseStop,
        OneMinuteHeikinAshiPrevLowDamage,
        OneMinuteHeikinAshiBearTurn,
        OneMinuteHeikinAshiBearTurnAndPrevLowDamage,
        OneMinuteHeikinAshiBearHold2,
        OneMinuteHeikinAshiPrevLowDamageOrBearHold2,
        OneMinuteHeikinAshiBearTurnAndMa5Damage,
        OneMinuteHeikinAshiBearTurnAndVolumeWeak
    }

    public enum FiveMinuteEntryMode
    {
        SignalCandleClose,
        OneMinuteMa5CrossMa60TouchMa60,
        OneMinuteMa5CrossMa20TouchMa20,
        OneMinuteMa5PreviousHighBreak,
        AvgifLowerOneMinutePrevHighLight,
        AvgifLowerOneMinutePrevHighNormal,
        AvgifLowerOneMinutePrevHighStrong
    }

    public sealed class FiveMinuteMa200MoneyBaseBacktest
    {
        public const string StrategyCode = "DAILY_ALIGNED_5M_PREDAY_RANGE_HALF_RSI2_CROSSUP";
        public const string OneMinuteMaTouchStrategyCode = "DAILY_ALIGNED_5M_PREDAY_RANGE_HALF_RSI2_1M_MA5_CROSS_MA60_TOUCH";
        public const string OneMinuteMa20TouchStrategyCode = "DAILY_ALIGNED_5M_PREDAY_RANGE_HALF_RSI2_1M_MA5_CROSS_MA20_TOUCH";
        public const string OneMinutePreviousHighBreakStrategyCode = "DAILY_ALIGNED_5M_PREDAY_RANGE_HALF_RSI2_1M_MA5_PREV_HIGH_BREAK";
        public const string AvgifLowerOneMinutePrevHighStrategyCode = "AVGIF240_LOWER_RECOVERY_1M_MA5_PREV_HIGH_BREAK";
        public const string HoldToNextDayExitRuleCode = "HOLD_TO_NEXT_TRADING_DAY_1100";
        public const string LinearRegressionExitRuleCode = "LINEAR_REGRESSION_5M_CROSSDOWN_OR_NEXT_DAY_1100";
        public const string Ma240DeviationUpperExitRuleCode = "MA240_POSITIVE_DEVIATION_UPPER_CROSSUP_OR_NEXT_DAY_1100";
        public const string SignalLowFiveMinuteCloseStopExitRuleCode = "SIGNAL_LOW_5M_CLOSE_STOP_OR_NEXT_DAY_1100";
        public const string Ma200FiveMinuteCloseStopExitRuleCode = "MA200_5M_CLOSE_STOP_OR_NEXT_DAY_1100";
        public const string OneMinutePreviousLowCloseStopExitRuleCode = "ONE_MINUTE_PREVIOUS_LOW_CLOSE_STOP_OR_NEXT_DAY_1100";
        public const string AvgifUpperOrOneMinutePreviousLowCloseStopExitRuleCode = "AVGIF240_UPPER_OR_ONE_MINUTE_PREVIOUS_LOW_CLOSE_STOP";
        public const string OneMinuteHeikinAshiExitRuleCodePrefix = "ONE_MINUTE_HEIKIN_ASHI";

        private const int MaxHoldingMinutes = 180;
        private const int PriorDailyBaseLookbackDays = 6;
        private const decimal PriorDailyBaseRiseRate = 25m;
        private const long PriorDailyBaseTradingValue = 70_000_000_000;

        private readonly BacktestDataStore _dataStore;
        private readonly BacktestRunStore _runStore;
        private readonly FiveMinuteExitMode _exitMode;
        private readonly FiveMinuteEntryMode _entryMode;
        private readonly decimal _rangeFactor;

        public FiveMinuteMa200MoneyBaseBacktest(
            FiveMinuteExitMode exitMode = FiveMinuteExitMode.HoldToNextDay1100,
            FiveMinuteEntryMode entryMode = FiveMinuteEntryMode.SignalCandleClose,
            decimal rangeFactor = 0.5m,
            BacktestDataStore? dataStore = null,
            BacktestRunStore? runStore = null)
        {
            _exitMode = exitMode;
            _entryMode = entryMode;
            _rangeFactor = Math.Clamp(rangeFactor, 0.1m, 0.9m);
            _dataStore = dataStore ?? new BacktestDataStore();
            _runStore = runStore ?? new BacktestRunStore();
        }

        public BacktestRunResult Run()
        {
            string factorLabel = FormatFactorForRunId(_rangeFactor);
            string runId = _runStore.CreateRunId(_exitMode switch
            {
                FiveMinuteExitMode.AvgifUpperOrOneMinutePreviousLowCloseStop => $"avgif240_lower_1m_prev_high_{ResolveAvgifVolumeFilterLabel(_entryMode)}_upper_or_prev_low_stop",
                _ when IsOneMinuteHeikinAshiExitMode(_exitMode) => $"avgif240_lower_1m_prev_high_{ResolveAvgifVolumeFilterLabel(_entryMode)}_{ResolveHeikinAshiExitLabel(_exitMode)}",
                FiveMinuteExitMode.LinearRegressionCrossDown => $"five_preday_range_f{factorLabel}_rsi2_lr_exit",
                FiveMinuteExitMode.Ma240PositiveDeviationUpperCrossUp => $"five_preday_range_f{factorLabel}_rsi2_ma240_upper_exit",
                FiveMinuteExitMode.OneMinutePreviousLowCloseStop => $"five_preday_range_f{factorLabel}_rsi2_1m_prev_high_break_prev_low_stop",
                FiveMinuteExitMode.SignalLowFiveMinuteCloseStop => $"five_preday_range_f{factorLabel}_rsi2_signal_low_5m_close_stop",
                FiveMinuteExitMode.Ma200FiveMinuteCloseStop => IsOneMinuteMaTouchMode(_entryMode)
                    ? $"five_preday_range_f{factorLabel}_rsi2_1m_ma{ResolveOneMinuteTouchPeriod(_entryMode)}_touch_ma200_stop"
                    : $"five_preday_range_f{factorLabel}_rsi2_ma200_stop",
                _ when IsOneMinuteMaTouchMode(_entryMode) => $"five_preday_range_f{factorLabel}_rsi2_1m_ma{ResolveOneMinuteTouchPeriod(_entryMode)}_touch_signal_low_stop",
                _ => $"five_preday_range_f{factorLabel}_rsi2_trigger"
            });
            List<BacktestSignalRow> signals = [];
            List<BacktestTradeRow> trades = [];
            List<RangeFactorDiagnosticRow> rangeFactorDiagnostics = [];
            List<ChartRequest> chartRequests = [];
            string strategyCode = ResolveStrategyCode();
            string exitRuleCode = ResolveExitRuleCode();

            foreach (StockMarketKey stock in EnumerateFiveMinuteStockMarkets())
            {
                List<BacktestDailyBar> dailyBars = [.. LoadDailyBarsForTrend(stock)
                    .OrderBy(item => BacktestDataStore.NormalizeDate(item.Date))];
                List<BacktestMinuteBar> oneBars = LoadMinuteBars(stock, 1);
                List<BacktestMinuteBar> fiveBars = LoadMinuteBars(stock, 5);
                List<BacktestMinuteBar> fifteenBars = LoadMinuteBars(stock, 15);

                if (dailyBars.Count < 61 || oneBars.Count < 25 || fiveBars.Count < 3 || fifteenBars.Count < 6)
                    continue;

                Dictionary<string, DailyTrendState> dailyTrendByDate = BuildDailyTrendStateMap(dailyBars);
                HashSet<string> priorDailyBaseBlockedDates = BuildPriorDailyBaseBlockedDates(dailyBars);
                Dictionary<string, long> dayOpenByDate = BuildDayOpenByDate(fiveBars);
                Dictionary<string, decimal> fiveRsi2ByTime = BuildRsiStateMap(fiveBars, 2);
                int oneMinuteTouchPeriod = ResolveOneMinuteTouchPeriod(_entryMode);
                Dictionary<string, MaState> oneMa5ByTime = IsOneMinuteMaTouchMode(_entryMode)
                    ? BuildMaStateMap(oneBars, 5)
                    : [];
                Dictionary<string, MaState> oneMaTargetByTime = IsOneMinuteMaTouchMode(_entryMode)
                    ? BuildMaStateMap(oneBars, oneMinuteTouchPeriod)
                    : [];
                HashSet<string> usedDate = new(StringComparer.Ordinal);

                for (int i = 1; i < fiveBars.Count; i++)
                {
                    BacktestMinuteBar baseBar = fiveBars[i];
                    BacktestMinuteBar previousBar = fiveBars[i - 1];
                    string date = ResolveDate(baseBar.DateTime);
                    if (string.IsNullOrWhiteSpace(date) || usedDate.Contains(date))
                        continue;

                    if (!string.Equals(date, ResolveDate(previousBar.DateTime), StringComparison.Ordinal))
                        continue;

                    if (!IsTradingWindow(baseBar.DateTime))
                        continue;

                    if (!dailyTrendByDate.TryGetValue(date, out DailyTrendState dailyTrend) ||
                        !dailyTrend.IsAligned)
                    {
                        continue;
                    }

                    if (!dayOpenByDate.TryGetValue(date, out long dayOpen) || dayOpen <= 0)
                        continue;

                    bool avgifMergedMode = IsAvgifMergedEntryMode(_entryMode);
                    decimal triggerLine;
                    decimal rsi2 = 0m;
                    string baseKind;
                    bool baseSignal;

                    if (avgifMergedMode)
                    {
                        if (!TryEvaluateMa240DeviationLowerRecovery(
                            fiveBars,
                            i,
                            out Ma240DeviationBandState bandState))
                        {
                            continue;
                        }

                        triggerLine = bandState.LowerLine;
                        baseSignal = bandState.LowerRecoveryCrossUp;
                        baseKind = "AVGIF240_LOWER_RECOVERY";
                    }
                    else
                    {
                        long previousRange = dailyTrend.PreviousHigh - dailyTrend.PreviousLow;
                        if (previousRange <= 0)
                            continue;

                        triggerLine = dayOpen + previousRange * _rangeFactor;
                        bool crossUp = previousBar.Close <= triggerLine && baseBar.Close > triggerLine;
                        bool rsiOk = fiveRsi2ByTime.TryGetValue(baseBar.DateTime, out rsi2) && rsi2 > 50m;
                        baseSignal = crossUp && rsiOk;
                        baseKind = "PREDAY_RANGE_HALF_RSI2_CROSSUP";
                    }

                    if (!baseSignal)
                        continue;

                    if (!avgifMergedMode && priorDailyBaseBlockedDates.Contains(date))
                        continue;

                    decimal ma200 = i >= 199
                        ? fiveBars.Skip(i - 199).Take(200).Average(bar => (decimal)bar.Close)
                        : 0m;
                    decimal ma5 = CalculateAverageClose(fiveBars, i, 5);
                    decimal ma20 = CalculateAverageClose(fiveBars, i, 20);
                    decimal ma60 = CalculateAverageClose(fiveBars, i, 60);
                    decimal volumeMa5 = CalculateAverageVolume(fiveBars, i, 5);
                    decimal volumeMa20 = CalculateAverageVolume(fiveBars, i, 20);
                    decimal volumeMa60 = CalculateAverageVolume(fiveBars, i, 60);
                    decimal tradingValueMa5 = CalculateAverageTradingValue(fiveBars, i, 5);
                    decimal tradingValueMa20 = CalculateAverageTradingValue(fiveBars, i, 20);
                    decimal tradingValueMa60 = CalculateAverageTradingValue(fiveBars, i, 60);
                    if (_exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop &&
                        (ma200 <= 0 || baseBar.Close <= ma200))
                    {
                        continue;
                    }

                    long stopPrice = dailyTrend.PreviousClose;
                    if (stopPrice <= 0)
                        continue;

                    BaseState baseState = new(
                        baseBar.DateTime,
                        baseBar.Open,
                        baseBar.High,
                        baseBar.Low,
                        baseBar.Close,
                        baseBar.Volume,
                        baseBar.TradingValue,
                        ma5,
                        ma20,
                        ma60,
                        ma200,
                        volumeMa5,
                        volumeMa20,
                        volumeMa60,
                        tradingValueMa5,
                        tradingValueMa20,
                        tradingValueMa60,
                        dailyTrend.Ma5,
                        dailyTrend.Ma20,
                        dailyTrend.Ma60,
                        dailyTrend.PreviousHigh,
                        dailyTrend.PreviousLow,
                        dailyTrend.DailyTradingValue,
                        dayOpen,
                        triggerLine,
                        rsi2,
                        stopPrice,
                        0,
                        0,
                        string.Empty,
                        0,
                        (baseBar.High + baseBar.Low) / 2m,
                        baseKind);

                    signals.Add(new BacktestSignalRow
                    {
                        RunId = runId,
                        StrategyCode = strategyCode,
                        Code = stock.Code,
                        Market = stock.Market,
                        SignalTime = baseBar.DateTime,
                        SignalType = "BASE",
                        Price = baseBar.Close,
                        Reason = BuildBaseReason(baseState)
                    });

                    EntryExitResult trade;
                    bool hasTrade = _entryMode == FiveMinuteEntryMode.OneMinuteMa5PreviousHighBreak || avgifMergedMode
                        ? TryBuildOneMinutePreviousHighBreakEntryAndExit(
                            baseState,
                            oneBars,
                            fiveBars,
                            out trade)
                        : IsOneMinuteMaTouchMode(_entryMode)
                        ? TryBuildOneMinuteMaTouchEntryAndExit(
                            baseState,
                            oneBars,
                            fiveBars,
                            oneMa5ByTime,
                            oneMaTargetByTime,
                            out trade)
                        : TryBuildFiveMinuteFormulaEntryAndExit(
                            baseState,
                            fiveBars,
                            out trade);

                    if (hasTrade)
                    {
                        signals.Add(new BacktestSignalRow
                        {
                            RunId = runId,
                            StrategyCode = strategyCode,
                            Code = stock.Code,
                            Market = stock.Market,
                            SignalTime = trade.EntryTime,
                            SignalType = "BUY",
                            Price = trade.EntryPrice,
                            Reason = trade.EntryReason
                        });

                        signals.Add(new BacktestSignalRow
                        {
                            RunId = runId,
                            StrategyCode = strategyCode,
                            Code = stock.Code,
                            Market = stock.Market,
                            SignalTime = trade.ExitTime,
                            SignalType = "SELL",
                            Price = trade.ExitPrice,
                            Reason = trade.ExitReason
                        });

                        BacktestTradeRow tradeRow = BuildTradeRow(runId, stock, trade, strategyCode, exitRuleCode);
                        trades.Add(tradeRow);
                        rangeFactorDiagnostics.Add(BuildRangeFactorDiagnostic(stock, baseState, tradeRow));
                        chartRequests.Add(new ChartRequest(stock, baseState.Time, trade.EntryTime, trade.ExitTime, trade.EntryPrice, trade.ExitPrice));
                    }

                    usedDate.Add(date);
                }
            }

            List<BacktestRunSummary> summaries =
            [
                BuildSummary(runId, strategyCode, exitRuleCode, "MIXED", signals, trades),
                BuildSummary(runId, strategyCode, exitRuleCode, "KRX", signals.Where(item => item.Market == "KRX").ToList(), trades.Where(item => item.Market == "KRX").ToList()),
                BuildSummary(runId, strategyCode, exitRuleCode, "NXT", signals.Where(item => item.Market == "NXT").ToList(), trades.Where(item => item.Market == "NXT").ToList()),
                BuildSummary(runId, strategyCode, exitRuleCode, "AL", signals.Where(item => item.Market == "AL").ToList(), trades.Where(item => item.Market == "AL").ToList())
            ];

            var config = new BacktestRunConfig
            {
                RunId = runId,
                BacktestMode = "SOR_ON",
                RunMode = "SOR_ON",
                MarketMode = "AL_ONLY",
                OrderMode = "None",
                LiveOrder = false,
                ExecutionType = "BacktestOnly",
                Memo = $"SOR ON integrated minute/daily bars use Market=AL when available. KRX previous-close/base-price and search/gate base-candle fields remain separate and are not overwritten. Test trigger replaces the prior 40eok/1m-MA60 rule: A=PREDAYHIGH()-PREDAYLOW(); B=DAYOPEN()+A*{_rangeFactor.ToString("0.##", CultureInfo.InvariantCulture)}; B1=RSI(2); CROSSUP(C,B) AND B1>50. Entry mode: {_entryMode}. Exit rule: {exitRuleCode}. MFE/exit reason show the best high reached during the holding window. DataStore is read-only."
            };

            string outputDirectory = _runStore.SaveRun(runId, signals, trades, summaries, config);
            SaveRangeFactorDiagnostics(outputDirectory, rangeFactorDiagnostics);
            SaveCharts(outputDirectory, chartRequests.Take(12));
            return new BacktestRunResult(runId, outputDirectory, signals.Count, trades.Count, summaries[0]);
        }

        private string ResolveExitRuleCode() => _exitMode switch
        {
            FiveMinuteExitMode.LinearRegressionCrossDown => LinearRegressionExitRuleCode,
            FiveMinuteExitMode.Ma240PositiveDeviationUpperCrossUp => Ma240DeviationUpperExitRuleCode,
            FiveMinuteExitMode.SignalLowFiveMinuteCloseStop => SignalLowFiveMinuteCloseStopExitRuleCode,
            FiveMinuteExitMode.Ma200FiveMinuteCloseStop => Ma200FiveMinuteCloseStopExitRuleCode,
            FiveMinuteExitMode.OneMinutePreviousLowCloseStop => OneMinutePreviousLowCloseStopExitRuleCode,
            FiveMinuteExitMode.AvgifUpperOrOneMinutePreviousLowCloseStop => AvgifUpperOrOneMinutePreviousLowCloseStopExitRuleCode,
            _ when IsOneMinuteHeikinAshiExitMode(_exitMode) => $"{OneMinuteHeikinAshiExitRuleCodePrefix}_{ResolveHeikinAshiExitLabel(_exitMode).ToUpperInvariant()}_OR_NEXT_DAY_1100",
            _ => HoldToNextDayExitRuleCode
        };

        private string ResolveStrategyCode() => _entryMode switch
        {
            FiveMinuteEntryMode.OneMinuteMa5CrossMa60TouchMa60 => OneMinuteMaTouchStrategyCode,
            FiveMinuteEntryMode.OneMinuteMa5CrossMa20TouchMa20 => OneMinuteMa20TouchStrategyCode,
            FiveMinuteEntryMode.OneMinuteMa5PreviousHighBreak => OneMinutePreviousHighBreakStrategyCode,
            _ when IsAvgifMergedEntryMode(_entryMode) => AvgifLowerOneMinutePrevHighStrategyCode,
            _ => StrategyCode
        };

        private static bool IsOneMinuteMaTouchMode(FiveMinuteEntryMode mode) =>
            mode == FiveMinuteEntryMode.OneMinuteMa5CrossMa60TouchMa60 ||
            mode == FiveMinuteEntryMode.OneMinuteMa5CrossMa20TouchMa20;

        private static bool IsAvgifMergedEntryMode(FiveMinuteEntryMode mode) =>
            mode == FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighLight ||
            mode == FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighNormal ||
            mode == FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighStrong;

        private static bool IsOneMinuteHeikinAshiExitMode(FiveMinuteExitMode mode) =>
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamage ||
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiBearTurn ||
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndPrevLowDamage ||
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiBearHold2 ||
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamageOrBearHold2 ||
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndMa5Damage ||
            mode == FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndVolumeWeak;

        private static string ResolveAvgifVolumeFilterLabel(FiveMinuteEntryMode mode) => mode switch
        {
            FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighLight => "light",
            FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighNormal => "normal",
            FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighStrong => "strong",
            _ => "none"
        };

        private static string ResolveHeikinAshiExitLabel(FiveMinuteExitMode mode) => mode switch
        {
            FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamage => "ha_prev_low_damage",
            FiveMinuteExitMode.OneMinuteHeikinAshiBearTurn => "ha_bear_turn",
            FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndPrevLowDamage => "ha_bear_turn_prev_low",
            FiveMinuteExitMode.OneMinuteHeikinAshiBearHold2 => "ha_bear_hold2",
            FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamageOrBearHold2 => "ha_prev_low_or_bear_hold2",
            FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndMa5Damage => "ha_bear_turn_ma5_damage",
            FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndVolumeWeak => "ha_bear_turn_volume_weak",
            _ => "ha_unknown"
        };

        private static int ResolveOneMinuteTouchPeriod(FiveMinuteEntryMode mode) =>
            mode == FiveMinuteEntryMode.OneMinuteMa5CrossMa20TouchMa20 ? 20 : 60;

        private static string FormatFactorForRunId(decimal factor) =>
            factor.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", "p");

        private IEnumerable<StockMarketKey> EnumerateFiveMinuteStockMarkets()
        {
            string directory = Path.Combine(_dataStore.RootPath, "minute", "5m");
            if (!Directory.Exists(directory))
                yield break;

            var stocks = new List<StockMarketKey>();
            foreach (string path in Directory.EnumerateFiles(directory, "*_5m.json").OrderBy(item => item, StringComparer.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                string code = BacktestDataStore.NormalizeCode(parts[0]);
                string market = BacktestDataStore.NormalizeMarket(parts[1]);
                if (string.IsNullOrWhiteSpace(code) || market is not ("KRX" or "NXT" or "AL"))
                    continue;

                stocks.Add(new StockMarketKey(code, market));
            }

            bool hasAl = stocks.Any(stock => string.Equals(stock.Market, "AL", StringComparison.Ordinal));
            foreach (StockMarketKey stock in stocks.Where(stock => !hasAl || string.Equals(stock.Market, "AL", StringComparison.Ordinal)))
                yield return stock;
        }

        private List<BacktestMinuteBar> LoadMinuteBars(StockMarketKey stock, int minute) =>
            [.. _dataStore.LoadMinuteBars(stock.Code, stock.Market, minute)
                .OrderBy(item => item.DateTime)];

        private IReadOnlyList<BacktestDailyBar> LoadDailyBarsForTrend(StockMarketKey stock)
        {
            if (!string.Equals(stock.Market, "AL", StringComparison.Ordinal))
                return _dataStore.LoadDailyBars(stock.Code, stock.Market);

            IReadOnlyList<BacktestDailyBar> alBars = _dataStore.LoadDailyBars(stock.Code, "AL");
            return alBars.Count > 0
                ? alBars
                : _dataStore.LoadDailyBars(stock.Code, "KRX");
        }

        private static Dictionary<string, DailyTrendState> BuildDailyTrendStateMap(IReadOnlyList<BacktestDailyBar> bars)
        {
            var result = new Dictionary<string, DailyTrendState>(StringComparer.Ordinal);
            for (int i = 60; i < bars.Count; i++)
            {
                BacktestDailyBar current = bars[i];
                BacktestDailyBar previous = bars[i - 1];

                List<decimal> previous5 = [.. bars.Skip(i - 5).Take(5).Select(bar => (decimal)bar.Close)];
                List<decimal> previous20 = [.. bars.Skip(i - 20).Take(20).Select(bar => (decimal)bar.Close)];
                List<decimal> previous60 = [.. bars.Skip(i - 60).Take(60).Select(bar => (decimal)bar.Close)];

                result[BacktestDataStore.NormalizeDate(current.Date)] = new DailyTrendState(
                    previous.High,
                    previous.Low,
                    previous.Close,
                    ResolveDailyTradingValue(current),
                    previous5.Average(),
                    previous20.Average(),
                    previous60.Average());
            }

            return result;
        }

        private static HashSet<string> BuildPriorDailyBaseBlockedDates(IReadOnlyList<BacktestDailyBar> bars)
        {
            HashSet<string> blocked = new(StringComparer.Ordinal);
            for (int i = 1; i < bars.Count; i++)
            {
                int start = Math.Max(1, i - PriorDailyBaseLookbackDays);
                for (int j = start; j < i; j++)
                {
                    if (!IsStrongPriorDailyBaseCandle(bars, j))
                        continue;

                    blocked.Add(BacktestDataStore.NormalizeDate(bars[i].Date));
                    break;
                }
            }

            return blocked;
        }

        private static bool IsStrongPriorDailyBaseCandle(IReadOnlyList<BacktestDailyBar> bars, int index)
        {
            if (index <= 0 || index >= bars.Count)
                return false;

            BacktestDailyBar current = bars[index];
            BacktestDailyBar previous = bars[index - 1];
            if (previous.Close <= 0)
                return false;

            decimal riseRate = (current.Close - previous.Close) / (decimal)previous.Close * 100m;
            return riseRate >= PriorDailyBaseRiseRate &&
                ResolveDailyTradingValue(current) >= PriorDailyBaseTradingValue;
        }

        private static long ResolveDailyTradingValue(BacktestDailyBar bar)
        {
            long estimated = bar.Close > 0 && bar.Volume > 0
                ? (long)Math.Min(long.MaxValue, bar.Close * (double)bar.Volume)
                : 0;
            return Math.Max(bar.TradingValue, estimated);
        }

        private static Dictionary<string, long> BuildDayOpenByDate(IReadOnlyList<BacktestMinuteBar> bars)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (BacktestMinuteBar bar in bars.OrderBy(item => item.DateTime))
            {
                string date = ResolveDate(bar.DateTime);
                if (string.IsNullOrWhiteSpace(date) || result.ContainsKey(date) || bar.Open <= 0)
                    continue;

                result[date] = bar.Open;
            }

            return result;
        }

        private static Dictionary<string, decimal> BuildRsiStateMap(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
            int resolved = Math.Max(1, period);
            for (int i = resolved; i < bars.Count; i++)
            {
                decimal gain = 0m;
                decimal loss = 0m;
                for (int j = i - resolved + 1; j <= i; j++)
                {
                    decimal diff = bars[j].Close - bars[j - 1].Close;
                    if (diff > 0)
                        gain += diff;
                    else
                        loss += Math.Abs(diff);
                }

                decimal rsi;
                if (gain <= 0 && loss <= 0)
                    rsi = 50m;
                else if (loss <= 0)
                    rsi = 100m;
                else if (gain <= 0)
                    rsi = 0m;
                else
                {
                    decimal rs = gain / loss;
                    rsi = 100m - 100m / (1m + rs);
                }

                result[bars[i].DateTime] = rsi;
            }

            return result;
        }

        private static Dictionary<string, WeeklyMacdSeed> BuildWeeklyMacdSeedMap(IReadOnlyList<BacktestDailyBar> bars)
        {
            List<WeeklyClose> weeklyCloses = [.. bars
                .Where(bar => !string.IsNullOrWhiteSpace(bar.Date) && bar.Close > 0)
                .GroupBy(bar => ResolveWeekStart(BacktestDataStore.NormalizeDate(bar.Date)), StringComparer.Ordinal)
                .Select(group =>
                {
                    BacktestDailyBar last = group
                        .OrderBy(bar => BacktestDataStore.NormalizeDate(bar.Date))
                        .Last();
                    return new WeeklyClose(BacktestDataStore.NormalizeDate(last.Date), last.Close);
                })
                .OrderBy(item => item.WeekEndDate)];

            List<WeeklyMacdSeed> weeklyStates = BuildWeeklyMacdSeeds(weeklyCloses);
            var result = new Dictionary<string, WeeklyMacdSeed>(StringComparer.Ordinal);

            foreach (BacktestDailyBar daily in bars)
            {
                string date = BacktestDataStore.NormalizeDate(daily.Date);
                WeeklyMacdSeed state = weeklyStates
                    .Where(item => string.CompareOrdinal(item.WeekEndDate, date) < 0)
                    .LastOrDefault();
                if (!string.IsNullOrWhiteSpace(state.WeekEndDate))
                    result[date] = state;
            }

            return result;
        }

        private static List<WeeklyMacdSeed> BuildWeeklyMacdSeeds(IReadOnlyList<WeeklyClose> weeklyCloses)
        {
            var result = new List<WeeklyMacdSeed>();
            decimal? ema12 = null;
            decimal? ema26 = null;
            decimal? signal = null;
            decimal alpha12 = 2m / 13m;
            decimal alpha26 = 2m / 27m;
            decimal alphaSignal = 2m / 10m;

            foreach (WeeklyClose close in weeklyCloses)
            {
                decimal value = close.Close;
                ema12 = ema12.HasValue ? value * alpha12 + ema12.Value * (1m - alpha12) : value;
                ema26 = ema26.HasValue ? value * alpha26 + ema26.Value * (1m - alpha26) : value;
                decimal macd = ema12.Value - ema26.Value;
                signal = signal.HasValue ? macd * alphaSignal + signal.Value * (1m - alphaSignal) : macd;
                result.Add(new WeeklyMacdSeed(close.WeekEndDate, ema12.Value, ema26.Value, signal.Value));
            }

            return result;
        }

        private static bool TryResolveWeeklyMacdAtSignal(
            IReadOnlyDictionary<string, WeeklyMacdSeed> seedsByDate,
            string signalTime,
            long signalPrice,
            out WeeklyMacdState state)
        {
            state = default;
            string signalDate = ResolveDate(signalTime);
            if (signalPrice <= 0 ||
                string.IsNullOrWhiteSpace(signalDate) ||
                !seedsByDate.TryGetValue(signalDate, out WeeklyMacdSeed seed))
            {
                return false;
            }

            const decimal alpha12 = 2m / 13m;
            const decimal alpha26 = 2m / 27m;
            const decimal alphaSignal = 2m / 10m;
            decimal price = signalPrice;
            decimal ema12 = price * alpha12 + seed.Ema12 * (1m - alpha12);
            decimal ema26 = price * alpha26 + seed.Ema26 * (1m - alpha26);
            decimal macd = ema12 - ema26;
            decimal signal = macd * alphaSignal + seed.Signal * (1m - alphaSignal);
            state = new WeeklyMacdState(seed.WeekEndDate, macd, signal);
            return true;
        }

        private Dictionary<string, IntradayRankState> BuildIntradayRankStateMap()
        {
            var snapshotsByTime = new Dictionary<string, List<IntradayRankSnapshot>>(StringComparer.Ordinal);

            foreach (StockMarketKey stock in EnumerateFiveMinuteStockMarkets())
            {
                List<BacktestMinuteBar> bars = LoadMinuteBars(stock, 1);
                if (bars.Count == 0)
                    continue;

                Dictionary<string, long> previousCloseByDate = LoadDailyBarsForTrend(stock)
                    .Where(bar => !string.IsNullOrWhiteSpace(bar.Date) && bar.PreviousClose > 0)
                    .GroupBy(bar => BacktestDataStore.NormalizeDate(bar.Date), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last().PreviousClose, StringComparer.Ordinal);

                long cumulativeTradingValue = 0;
                string currentDate = string.Empty;
                foreach (BacktestMinuteBar bar in bars)
                {
                    string date = ResolveDate(bar.DateTime);
                    if (string.IsNullOrWhiteSpace(date))
                        continue;

                    if (!string.Equals(currentDate, date, StringComparison.Ordinal))
                    {
                        currentDate = date;
                        cumulativeTradingValue = 0;
                    }

                    cumulativeTradingValue += Math.Max(0, bar.TradingValue);
                    long previousClose = previousCloseByDate.TryGetValue(date, out long resolvedPreviousClose)
                        ? resolvedPreviousClose
                        : 0;
                    decimal changeRate = previousClose > 0
                        ? (bar.Close - previousClose) / (decimal)previousClose * 100m
                        : 0m;

                    if (!snapshotsByTime.TryGetValue(bar.DateTime, out List<IntradayRankSnapshot>? snapshots))
                    {
                        snapshots = [];
                        snapshotsByTime[bar.DateTime] = snapshots;
                    }

                    snapshots.Add(new IntradayRankSnapshot(stock, cumulativeTradingValue, changeRate));
                }
            }

            var result = new Dictionary<string, IntradayRankState>(StringComparer.Ordinal);
            foreach ((string time, List<IntradayRankSnapshot> snapshots) in snapshotsByTime)
            {
                Dictionary<StockMarketKey, int> tradingValueRanks = snapshots
                    .OrderByDescending(item => item.CumulativeTradingValue)
                    .Take(10)
                    .Select((item, index) => new { item.Stock, Rank = index + 1 })
                    .ToDictionary(item => item.Stock, item => item.Rank);

                Dictionary<StockMarketKey, int> changeRateRanks = snapshots
                    .OrderByDescending(item => item.ChangeRate)
                    .Take(10)
                    .Select((item, index) => new { item.Stock, Rank = index + 1 })
                    .ToDictionary(item => item.Stock, item => item.Rank);

                foreach (IntradayRankSnapshot snapshot in snapshots)
                {
                    int tradingValueRank = tradingValueRanks.TryGetValue(snapshot.Stock, out int valueRank) ? valueRank : 0;
                    int changeRateRank = changeRateRanks.TryGetValue(snapshot.Stock, out int rateRank) ? rateRank : 0;
                    if (tradingValueRank <= 0 && changeRateRank <= 0)
                        continue;

                    result[BuildIntradayRankKey(snapshot.Stock, time)] = new IntradayRankState(
                        time,
                        snapshot.CumulativeTradingValue,
                        snapshot.ChangeRate,
                        tradingValueRank,
                        changeRateRank);
                }
            }

            return result;
        }

        private static bool TryGetIntradayRankState(
            IReadOnlyDictionary<string, IntradayRankState> states,
            StockMarketKey stock,
            string time,
            out IntradayRankState state) =>
            states.TryGetValue(BuildIntradayRankKey(stock, time), out state);

        private static string BuildIntradayRankKey(StockMarketKey stock, string time) =>
            $"{time}|{stock.Code}|{stock.Market}";

        private static string ResolveWeekStart(string date)
        {
            if (!DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return date;

            int diff = ((int)parsed.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return parsed.AddDays(-diff).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        private static bool TryFindOneMinuteMa60RecoverEntryAndExit(
            StockMarketKey stock,
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyDictionary<string, MaState> oneMa5ByTime,
            IReadOnlyDictionary<string, MaState> oneMa60ByTime,
            IReadOnlyDictionary<string, MaState> fifteenStateByTime,
            IReadOnlyDictionary<string, WeeklyMacdSeed> weeklyMacdSeedByDate,
            out EntryExitResult result)
        {
            result = default;
            int baseIndex = FindTimeIndex(oneBars, baseState.Time);
            if (baseIndex < 20 || baseIndex + 1 >= oneBars.Count)
                return false;

            string baseDate = ResolveDate(baseState.Time);
            RecoveryState? recovery = null;
            for (int i = baseIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (!string.Equals(baseDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                int elapsed = ResolveHoldingMinutes(baseState.Time, current.DateTime, i - baseIndex);
                if (elapsed > MaxHoldingMinutes)
                    break;

                bool bullish = current.Close > current.Open;
                bool ma60Recover = TryGetLatestMaState(oneMa60ByTime, current.DateTime, out MaState oneMa60) &&
                    oneMa60.PreviousClose <= oneMa60.PreviousMa &&
                    oneMa60.Close > oneMa60.Ma;

                if (!recovery.HasValue)
                {
                    if (bullish && ma60Recover)
                        recovery = new RecoveryState(current.DateTime, current.High, oneMa60.Ma);

                    continue;
                }

                RecoveryState resolvedRecovery = recovery.Value;
                bool recoveryHighBreak = current.Close > resolvedRecovery.High;
                if (!bullish || !recoveryHighBreak)
                    continue;

                if (!TryResolveWeeklyMacdAtSignal(weeklyMacdSeedByDate, current.DateTime, current.Close, out WeeklyMacdState weeklyMacd) ||
                    !weeklyMacd.IsBullish)
                {
                    continue;
                }

                BaseState entryBaseState = baseState with
                {
                    WeeklyMacd = weeklyMacd.Macd,
                    WeeklySignal = weeklyMacd.Signal,
                    RecoveryTime = resolvedRecovery.Time,
                    RecoveryHigh = resolvedRecovery.High
                };

                if (!TryResolveSignalExit(
                    current,
                    entryBaseState,
                    oneBars,
                    i,
                    oneMa5ByTime,
                    fifteenStateByTime,
                    out ExitResult exit))
                {
                    return false;
                }

                result = new EntryExitResult(
                    current.DateTime,
                    current.Close,
                    exit.ExitTime,
                    exit.ExitPrice,
                    exit.MaxHigh,
                    exit.MinLow,
                    entryBaseState.Low,
                    exit.HoldingMinutes,
                    BuildEntryReason(entryBaseState, resolvedRecovery.Ma60),
                    exit.ExitReason);
                return true;
            }

            return false;
        }

        private bool TryBuildFiveMinuteFormulaEntryAndExit(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> fiveBars,
            out EntryExitResult result)
        {
            result = default;
            int entryIndex = FindTimeIndex(fiveBars, baseState.Time);
            if (entryIndex < 0 || entryIndex + 1 >= fiveBars.Count)
                return false;

            string entryDate = ResolveDate(baseState.Time);
            long maxHigh = baseState.High;
            long minLow = baseState.Low;
            long target3Price = baseState.Close > 0
                ? (long)Math.Ceiling(baseState.Close * 1.03m)
                : 0;
            long structuralStopPrice = _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                ? baseState.Low
                : _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                    ? (long)Math.Round(baseState.Ma200, MidpointRounding.AwayFromZero)
                    : baseState.StopPrice;
            string structuralStopLabel = _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                ? "signalLow5mCloseStop"
                : _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                    ? "ma2005mCloseStop"
                    : "prevCloseStop";
            string firstVerdict = string.Empty;
            string target3Time = string.Empty;
            string stopTime = string.Empty;
            string targetExitDate = string.Empty;

            for (int i = entryIndex + 1; i < fiveBars.Count; i++)
            {
                BacktestMinuteBar current = fiveBars[i];
                string currentDate = ResolveDate(current.DateTime);
                if (string.IsNullOrWhiteSpace(currentDate))
                    continue;

                if (!string.Equals(entryDate, currentDate, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(targetExitDate))
                {
                    targetExitDate = currentDate;
                }

                if (!string.IsNullOrWhiteSpace(targetExitDate) &&
                    !string.Equals(currentDate, targetExitDate, StringComparison.Ordinal))
                {
                    break;
                }

                int holdingMinutes = ResolveHoldingMinutes(baseState.Time, current.DateTime, i - entryIndex);
                if (holdingMinutes <= 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                if (string.IsNullOrWhiteSpace(firstVerdict))
                {
                    bool target3Hit = target3Price > 0 && current.High >= target3Price;
                    long currentMa200Stop = ResolveMaAt(fiveBars, i, 200);
                    bool structuralStopHit = structuralStopPrice > 0 &&
                        (_exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                            ? currentMa200Stop > 0 && current.Close < currentMa200Stop
                            : _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                            ? current.Close < structuralStopPrice
                            : current.Low < structuralStopPrice);

                    if (target3Hit && structuralStopHit)
                    {
                        firstVerdict = "BOTH_SAME_BAR";
                        target3Time = current.DateTime;
                        stopTime = current.DateTime;
                    }
                    else if (target3Hit)
                    {
                        firstVerdict = "TARGET_3_FIRST";
                        target3Time = current.DateTime;
                    }
                    else if (structuralStopHit)
                    {
                        firstVerdict = _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                            ? "MA200_5M_CLOSE_STOP_FIRST"
                            : _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                            ? "SIGNAL_LOW_5M_CLOSE_STOP_FIRST"
                            : "PREV_CLOSE_STOP_FIRST";
                        stopTime = current.DateTime;
                    }
                }

                long currentMa200 = _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                    ? ResolveMaAt(fiveBars, i, 200)
                    : 0;

                if (_exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop &&
                    currentMa200 > 0 &&
                    current.Close < currentMa200)
                {
                    result = new EntryExitResult(
                        baseState.Time,
                        baseState.Close,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        currentMa200,
                        holdingMinutes,
                        BuildEntryReason(baseState),
                        BuildFiveMinuteExitReason(
                            "5m close below MA200",
                            maxHigh,
                            baseState.Close,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            currentMa200,
                            stopTime,
                            structuralStopLabel,
                            default,
                            default));
                    return true;
                }

                if (_exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop &&
                    structuralStopPrice > 0 &&
                    current.Close < structuralStopPrice)
                {
                    result = new EntryExitResult(
                        baseState.Time,
                        baseState.Close,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        structuralStopPrice,
                        holdingMinutes,
                        BuildEntryReason(baseState),
                        BuildFiveMinuteExitReason(
                            "5m close below signal candle low",
                            maxHigh,
                            baseState.Close,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            structuralStopPrice,
                            stopTime,
                            structuralStopLabel,
                            default,
                            default));
                    return true;
                }

                if (_exitMode == FiveMinuteExitMode.LinearRegressionCrossDown &&
                    TryEvaluateLinearRegressionExit(fiveBars, i, out LinearRegressionExitState regressionExit) &&
                    regressionExit.ShouldExit)
                {
                    result = new EntryExitResult(
                        baseState.Time,
                        baseState.Close,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        structuralStopPrice,
                        holdingMinutes,
                        BuildEntryReason(baseState),
                        BuildFiveMinuteExitReason(
                            "linear regression 5m crossdown",
                            maxHigh,
                            baseState.Close,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            structuralStopPrice,
                            stopTime,
                            structuralStopLabel,
                            regressionExit,
                            default));
                    return true;
                }

                if (_exitMode == FiveMinuteExitMode.Ma240PositiveDeviationUpperCrossUp &&
                    TryEvaluateMa240DeviationUpperExit(fiveBars, i, out Ma240DeviationUpperExitState ma240UpperExit) &&
                    ma240UpperExit.ShouldExit)
                {
                    result = new EntryExitResult(
                        baseState.Time,
                        baseState.Close,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        structuralStopPrice,
                        holdingMinutes,
                        BuildEntryReason(baseState),
                        BuildFiveMinuteExitReason(
                            "MA240 positive deviation upper crossup",
                            maxHigh,
                            baseState.Close,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            structuralStopPrice,
                            stopTime,
                            structuralStopLabel,
                            default,
                            ma240UpperExit));
                    return true;
                }

                if (string.IsNullOrWhiteSpace(targetExitDate) ||
                    string.CompareOrdinal(ResolveTime(current.DateTime), "110000") < 0)
                {
                    continue;
                }

                result = new EntryExitResult(
                    baseState.Time,
                    baseState.Close,
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    structuralStopPrice,
                    holdingMinutes,
                    BuildEntryReason(baseState),
                    BuildFiveMinuteExitReason(
                        "hold to next trading day 11:00 close",
                        maxHigh,
                        baseState.Close,
                        firstVerdict,
                        target3Price,
                        target3Time,
                        structuralStopPrice,
                        stopTime,
                        structuralStopLabel,
                        default,
                        default));
                return true;
            }

            return false;
        }

        private bool TryBuildOneMinuteMaTouchEntryAndExit(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyList<BacktestMinuteBar> fiveBars,
            IReadOnlyDictionary<string, MaState> oneMa5ByTime,
            IReadOnlyDictionary<string, MaState> oneMaTargetByTime,
            out EntryExitResult result)
        {
            result = default;
            int touchPeriod = ResolveOneMinuteTouchPeriod(_entryMode);
            int baseIndex = FindTimeIndex(oneBars, baseState.Time);
            if (baseIndex < touchPeriod || baseIndex + 1 >= oneBars.Count)
                return false;

            string baseDate = ResolveDate(baseState.Time);
            bool maCrossSeen = false;
            string crossTime = string.Empty;

            for (int i = baseIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (!string.Equals(baseDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                int elapsed = ResolveHoldingMinutes(baseState.Time, current.DateTime, i - baseIndex);
                if (elapsed > MaxHoldingMinutes)
                    break;

                if (!TryGetLatestMaState(oneMa5ByTime, current.DateTime, out MaState ma5) ||
                    !TryGetLatestMaState(oneMaTargetByTime, current.DateTime, out MaState maTarget) ||
                    maTarget.Ma <= 0)
                {
                    continue;
                }

                bool ma5CrossUpTarget = ma5.PreviousMa <= maTarget.PreviousMa && ma5.Ma > maTarget.Ma;
                if (!maCrossSeen)
                {
                    if (ma5CrossUpTarget)
                    {
                        maCrossSeen = true;
                        crossTime = current.DateTime;
                    }

                    continue;
                }

                bool aboveBaseLow = current.Low >= baseState.Low && maTarget.Ma > baseState.Low;
                bool touchedMa = current.Low <= maTarget.Ma && current.High >= maTarget.Ma;
                bool closedAboveMa = current.Close >= maTarget.Ma;
                if (!aboveBaseLow || !touchedMa || !closedAboveMa)
                    continue;

                long entryPrice = Math.Max(1, (long)Math.Round(maTarget.Ma, MidpointRounding.AwayFromZero));
                BaseState entryBaseState = baseState with
                {
                    RecoveryTime = crossTime,
                    RecoveryHigh = current.High,
                    Close = entryPrice
                };

                string entryReason =
                    $"1m MA5 crossed above MA{touchPeriod} at {crossTime}; buy on 1m MA{touchPeriod} touch above 5m signal low; entry {entryPrice:N0}; touch bar {current.DateTime}; 1m MA5 {ma5.Ma:N0}; 1m MA{touchPeriod} {maTarget.Ma:N0}; base {baseState.Time}; base low {baseState.Low:N0}; B={baseState.TriggerLine:N0}; RSI2={baseState.Rsi2:0.##}";

                if (!TryResolveFiveMinuteExitFromEntry(
                    entryBaseState,
                    fiveBars,
                    current.DateTime,
                    entryPrice,
                    entryReason,
                    out result))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private bool TryBuildOneMinutePreviousHighBreakEntryAndExit(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyList<BacktestMinuteBar> fiveBars,
            out EntryExitResult result)
        {
            result = default;
            int baseIndex = FindTimeIndex(oneBars, baseState.Time);
            if (baseIndex < 5 || baseIndex + 1 >= oneBars.Count)
                return false;

            string baseDate = ResolveDate(baseState.Time);
            for (int i = baseIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                BacktestMinuteBar previous = oneBars[i - 1];
                if (!string.Equals(baseDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                int elapsed = ResolveHoldingMinutes(baseState.Time, current.DateTime, i - baseIndex);
                if (elapsed > MaxHoldingMinutes)
                    break;

                decimal ma5 = ResolveMaAt(oneBars, i, 5);
                decimal volumeMa5 = CalculateAverageVolume(oneBars, i, 5);
                decimal volumeMa10 = CalculateAverageVolume(oneBars, i, 10);
                decimal previousVolumeMa5 = CalculateAverageVolume(oneBars, i - 1, 5);
                bool bullish = current.Close > current.Open;
                bool closeAboveMa5 = ma5 > 0 && current.Close > ma5;
                bool previousHighBreak = current.Close > previous.High;
                bool volumeMaintained = volumeMa5 > 0 &&
                    previousVolumeMa5 > 0 &&
                    volumeMa5 >= previousVolumeMa5 &&
                    current.Volume >= volumeMa5 * 0.8m;
                bool avgifMergedMode = IsAvgifMergedEntryMode(_entryMode);
                string volumeFilterLabel = "maintained";
                bool volumeOk = avgifMergedMode
                    ? ResolveAvgifVolumeFilter(
                        _entryMode,
                        current,
                        volumeMa5,
                        previousVolumeMa5,
                        volumeMa10,
                        out volumeFilterLabel)
                    : volumeMaintained;

                if (!bullish || !closeAboveMa5 || !previousHighBreak || !volumeOk)
                    continue;

                long entryPrice = current.Close;
                BaseState entryBaseState = baseState with
                {
                    RecoveryTime = current.DateTime,
                    RecoveryHigh = current.High,
                    Close = entryPrice
                };

                string entryReason =
                    $"1m completed close broke previous high above MA5 with volume {volumeFilterLabel}; bar {current.DateTime}; close {current.Close:N0}; prevHigh {previous.High:N0}; MA5 {ma5:N0}; volume {current.Volume:N0}; volumeMA5 {volumeMa5:N0}; volumeMA10 {volumeMa10:N0}; base {baseState.Time}; trigger={baseState.TriggerLine:N0}; kind={baseState.Kind}; RSI2={baseState.Rsi2:0.##}";

                if (_exitMode == FiveMinuteExitMode.AvgifUpperOrOneMinutePreviousLowCloseStop)
                {
                    if (!TryResolveAvgifMergedExit(
                        entryBaseState,
                        oneBars,
                        fiveBars,
                        i,
                        entryPrice,
                        entryReason,
                        out result))
                    {
                        return false;
                    }
                }
                else if (IsOneMinuteHeikinAshiExitMode(_exitMode))
                {
                    if (!TryResolveHeikinAshiOneMinuteExit(
                        entryBaseState,
                        oneBars,
                        i,
                        entryPrice,
                        entryReason,
                        out result))
                    {
                        return false;
                    }
                }
                else if (_exitMode == FiveMinuteExitMode.OneMinutePreviousLowCloseStop)
                {
                    if (!TryResolveOneMinutePreviousLowCloseExit(
                        entryBaseState,
                        oneBars,
                        i,
                        entryPrice,
                        entryReason,
                        out result))
                    {
                        return false;
                    }
                }
                else if (!TryResolveFiveMinuteExitFromEntry(
                    entryBaseState,
                    fiveBars,
                    current.DateTime,
                    entryPrice,
                    entryReason,
                    out result))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool ResolveAvgifVolumeFilter(
            FiveMinuteEntryMode entryMode,
            BacktestMinuteBar current,
            decimal volumeMa5,
            decimal previousVolumeMa5,
            decimal volumeMa10,
            out string label)
        {
            label = ResolveAvgifVolumeFilterLabel(entryMode);
            if (volumeMa5 <= 0)
                return false;

            return entryMode switch
            {
                FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighLight =>
                    current.Volume >= volumeMa5 * 0.7m,
                FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighNormal =>
                    current.Volume >= volumeMa5,
                FiveMinuteEntryMode.AvgifLowerOneMinutePrevHighStrong =>
                    previousVolumeMa5 > 0 &&
                    volumeMa10 > 0 &&
                    current.Volume >= volumeMa5 &&
                    volumeMa5 > previousVolumeMa5 &&
                    volumeMa5 >= volumeMa10,
                _ => false
            };
        }

        private static bool TryResolveOneMinutePreviousLowCloseExit(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            int entryIndex,
            long entryPrice,
            string entryReason,
            out EntryExitResult result)
        {
            result = default;
            if (entryIndex < 1 || entryIndex + 1 >= oneBars.Count)
                return false;

            string entryTime = oneBars[entryIndex].DateTime;
            string entryDate = ResolveDate(entryTime);
            long maxHigh = Math.Max(entryPrice, oneBars[entryIndex].High);
            long minLow = Math.Min(entryPrice, oneBars[entryIndex].Low);
            long target3Price = entryPrice > 0
                ? (long)Math.Ceiling(entryPrice * 1.03m)
                : 0;
            string firstVerdict = string.Empty;
            string target3Time = string.Empty;
            string stopTime = string.Empty;
            string targetExitDate = string.Empty;
            long stopPrice = oneBars[entryIndex - 1].Low;

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                BacktestMinuteBar previous = oneBars[i - 1];
                string currentDate = ResolveDate(current.DateTime);
                if (string.IsNullOrWhiteSpace(currentDate))
                    continue;

                if (!string.Equals(entryDate, currentDate, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(targetExitDate))
                {
                    targetExitDate = currentDate;
                }

                if (!string.IsNullOrWhiteSpace(targetExitDate) &&
                    !string.Equals(currentDate, targetExitDate, StringComparison.Ordinal))
                {
                    break;
                }

                int holdingMinutes = ResolveHoldingMinutes(entryTime, current.DateTime, i - entryIndex);
                if (holdingMinutes < 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                bool target3Hit = target3Price > 0 && current.High >= target3Price;
                bool previousLowCloseStopHit = current.Close < previous.Low;
                if (string.IsNullOrWhiteSpace(firstVerdict))
                {
                    if (target3Hit && previousLowCloseStopHit)
                    {
                        firstVerdict = "BOTH_SAME_BAR";
                        target3Time = current.DateTime;
                        stopTime = current.DateTime;
                    }
                    else if (target3Hit)
                    {
                        firstVerdict = "TARGET_3_FIRST";
                        target3Time = current.DateTime;
                    }
                    else if (previousLowCloseStopHit)
                    {
                        firstVerdict = "ONE_MINUTE_PREV_LOW_CLOSE_STOP_FIRST";
                        stopTime = current.DateTime;
                    }
                }

                if (previousLowCloseStopHit)
                {
                    stopPrice = previous.Low;
                    result = new EntryExitResult(
                        entryTime,
                        entryPrice,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        stopPrice,
                        Math.Max(0, holdingMinutes),
                        entryReason,
                        BuildOneMinuteExitReason(
                            "1m completed close below previous low",
                            maxHigh,
                            entryPrice,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            stopPrice,
                            stopTime));
                    return true;
                }

                if (string.IsNullOrWhiteSpace(targetExitDate) ||
                    string.CompareOrdinal(ResolveTime(current.DateTime), "110000") < 0)
                {
                    continue;
                }

                result = new EntryExitResult(
                    entryTime,
                    entryPrice,
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    stopPrice,
                    Math.Max(0, holdingMinutes),
                    entryReason,
                    BuildOneMinuteExitReason(
                        "hold to next trading day 11:00 close",
                        maxHigh,
                        entryPrice,
                        firstVerdict,
                        target3Price,
                        target3Time,
                        stopPrice,
                        stopTime));
                return true;
            }

            return false;
        }

        private static bool TryResolveAvgifMergedExit(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            IReadOnlyList<BacktestMinuteBar> fiveBars,
            int entryIndex,
            long entryPrice,
            string entryReason,
            out EntryExitResult result)
        {
            result = default;
            if (entryIndex < 1 || entryIndex + 1 >= oneBars.Count)
                return false;

            string entryTime = oneBars[entryIndex].DateTime;
            string entryDate = ResolveDate(entryTime);
            long maxHigh = Math.Max(entryPrice, oneBars[entryIndex].High);
            long minLow = Math.Min(entryPrice, oneBars[entryIndex].Low);
            long target3Price = entryPrice > 0
                ? (long)Math.Ceiling(entryPrice * 1.03m)
                : 0;
            string firstVerdict = string.Empty;
            string target3Time = string.Empty;
            string stopTime = string.Empty;
            string targetExitDate = string.Empty;
            long stopPrice = oneBars[entryIndex - 1].Low;
            int lastCheckedFiveIndex = -1;

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                BacktestMinuteBar previous = oneBars[i - 1];
                string currentDate = ResolveDate(current.DateTime);
                if (string.IsNullOrWhiteSpace(currentDate))
                    continue;

                if (!string.Equals(entryDate, currentDate, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(targetExitDate))
                {
                    targetExitDate = currentDate;
                }

                if (!string.IsNullOrWhiteSpace(targetExitDate) &&
                    !string.Equals(currentDate, targetExitDate, StringComparison.Ordinal))
                {
                    break;
                }

                int holdingMinutes = ResolveHoldingMinutes(entryTime, current.DateTime, i - entryIndex);
                if (holdingMinutes < 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                bool target3Hit = target3Price > 0 && current.High >= target3Price;
                bool previousLowCloseStopHit = current.Close < previous.Low;
                bool avgifUpperHit = false;
                Ma240DeviationBandState upperState = default;
                int fiveIndex = FindLastTimeIndexAtOrBefore(fiveBars, current.DateTime);
                if (fiveIndex > 0 && fiveIndex != lastCheckedFiveIndex)
                {
                    lastCheckedFiveIndex = fiveIndex;
                    avgifUpperHit =
                        TryEvaluateMa240DeviationUpperExtension(fiveBars, fiveIndex, out upperState) &&
                        upperState.UpperExtensionCrossUp;
                }

                if (string.IsNullOrWhiteSpace(firstVerdict))
                {
                    if (target3Hit)
                    {
                        firstVerdict = "TARGET_3_FIRST";
                        target3Time = current.DateTime;
                    }
                    else if (avgifUpperHit)
                    {
                        firstVerdict = "AVGIF240_UPPER_FIRST";
                        target3Time = current.DateTime;
                    }
                    else if (previousLowCloseStopHit)
                    {
                        firstVerdict = "ONE_MINUTE_PREV_LOW_CLOSE_STOP_FIRST";
                        stopTime = current.DateTime;
                    }
                }

                if (avgifUpperHit)
                {
                    result = new EntryExitResult(
                        entryTime,
                        entryPrice,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        stopPrice,
                        Math.Max(0, holdingMinutes),
                        entryReason,
                        BuildOneMinuteExitReason(
                            $"AVGIF240 upper extension crossup; upper={upperState.UpperLine:N0}; MA240={upperState.Ma240:N0}",
                            maxHigh,
                            entryPrice,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            stopPrice,
                            stopTime));
                    return true;
                }

                if (previousLowCloseStopHit)
                {
                    stopPrice = previous.Low;
                    result = new EntryExitResult(
                        entryTime,
                        entryPrice,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        stopPrice,
                        Math.Max(0, holdingMinutes),
                        entryReason,
                        BuildOneMinuteExitReason(
                            "1m completed close below previous low",
                            maxHigh,
                            entryPrice,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            stopPrice,
                            stopTime));
                    return true;
                }

                if (string.IsNullOrWhiteSpace(targetExitDate) ||
                    string.CompareOrdinal(ResolveTime(current.DateTime), "110000") < 0)
                {
                    continue;
                }

                result = new EntryExitResult(
                    entryTime,
                    entryPrice,
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    stopPrice,
                    Math.Max(0, holdingMinutes),
                    entryReason,
                    BuildOneMinuteExitReason(
                        "hold to next trading day 11:00 close",
                        maxHigh,
                        entryPrice,
                        firstVerdict,
                        target3Price,
                        target3Time,
                        stopPrice,
                        stopTime));
                return true;
            }

            return false;
        }

        private bool TryResolveHeikinAshiOneMinuteExit(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            int entryIndex,
            long entryPrice,
            string entryReason,
            out EntryExitResult result)
        {
            result = default;
            if (entryIndex < 1 || entryIndex + 1 >= oneBars.Count)
                return false;

            decimal[] haOpen = new decimal[oneBars.Count];
            decimal[] haClose = new decimal[oneBars.Count];
            for (int i = 0; i < oneBars.Count; i++)
            {
                BacktestMinuteBar bar = oneBars[i];
                haClose[i] = (bar.Open + bar.High + bar.Low + bar.Close) / 4m;
                haOpen[i] = i == 0
                    ? (bar.Open + bar.Close) / 2m
                    : (haOpen[i - 1] + haClose[i - 1]) / 2m;
            }

            string entryTime = oneBars[entryIndex].DateTime;
            string entryDate = ResolveDate(entryTime);
            long maxHigh = Math.Max(entryPrice, oneBars[entryIndex].High);
            long minLow = Math.Min(entryPrice, oneBars[entryIndex].Low);
            long target3Price = entryPrice > 0
                ? (long)Math.Ceiling(entryPrice * 1.03m)
                : 0;
            string firstVerdict = string.Empty;
            string target3Time = string.Empty;
            string stopTime = string.Empty;
            string targetExitDate = string.Empty;
            long stopPrice = oneBars[entryIndex - 1].Low;

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                BacktestMinuteBar previous = oneBars[i - 1];
                string currentDate = ResolveDate(current.DateTime);
                if (string.IsNullOrWhiteSpace(currentDate))
                    continue;

                if (!string.Equals(entryDate, currentDate, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(targetExitDate))
                {
                    targetExitDate = currentDate;
                }

                if (!string.IsNullOrWhiteSpace(targetExitDate) &&
                    !string.Equals(currentDate, targetExitDate, StringComparison.Ordinal))
                {
                    break;
                }

                int holdingMinutes = ResolveHoldingMinutes(entryTime, current.DateTime, i - entryIndex);
                if (holdingMinutes < 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                bool target3Hit = target3Price > 0 && current.High >= target3Price;
                bool haBear0 = haClose[i] < haOpen[i];
                bool haBear1 = haClose[i - 1] < haOpen[i - 1];
                bool haBull1 = haClose[i - 1] >= haOpen[i - 1];
                bool haBearTurn = haBull1 && haBear0;
                bool haBearHold2 = haBear0 && haBear1;
                bool previousLowDamage = current.Close < previous.Low;
                long ma5 = ResolveMaAt(oneBars, i, 5);
                decimal volumeMa5 = CalculateAverageVolume(oneBars, i, 5);
                bool ma5Damage = ma5 > 0 && current.Close < ma5;
                bool volumeWeak = volumeMa5 > 0 && current.Close < previous.Close && current.Volume < volumeMa5;
                bool haBullTurn = haBear1 && haClose[i] >= haOpen[i];
                bool haBullHold2 = haClose[i] >= haOpen[i] && haClose[i - 1] >= haOpen[i - 1];
                bool exitHit = _exitMode switch
                {
                    FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamage => previousLowDamage,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurn => haBearTurn,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndPrevLowDamage => haBearTurn && previousLowDamage,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearHold2 => haBearHold2,
                    FiveMinuteExitMode.OneMinuteHeikinAshiPrevLowDamageOrBearHold2 => previousLowDamage || haBearHold2,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndMa5Damage => haBearTurn && ma5Damage,
                    FiveMinuteExitMode.OneMinuteHeikinAshiBearTurnAndVolumeWeak => haBearTurn && volumeWeak,
                    _ => false
                };

                if (string.IsNullOrWhiteSpace(firstVerdict))
                {
                    if (target3Hit && exitHit)
                    {
                        firstVerdict = "BOTH_SAME_BAR";
                        target3Time = current.DateTime;
                        stopTime = current.DateTime;
                    }
                    else if (target3Hit)
                    {
                        firstVerdict = "TARGET_3_FIRST";
                        target3Time = current.DateTime;
                    }
                    else if (exitHit)
                    {
                        firstVerdict = ResolveHeikinAshiExitLabel(_exitMode).ToUpperInvariant() + "_FIRST";
                        stopTime = current.DateTime;
                    }
                }

                if (exitHit)
                {
                    stopPrice = previousLowDamage
                        ? previous.Low
                        : ma5Damage
                            ? ma5
                            : current.Close;
                    result = new EntryExitResult(
                        entryTime,
                        entryPrice,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        stopPrice,
                        Math.Max(0, holdingMinutes),
                        entryReason,
                        BuildOneMinuteExitReason(
                            $"{ResolveHeikinAshiExitLabel(_exitMode)}; HA O/C {haOpen[i]:N2}/{haClose[i]:N2}; prevLowDamage={(previousLowDamage ? "Y" : "N")}; ma5Damage={(ma5Damage ? "Y" : "N")} MA5={ma5:N0}; volumeWeak={(volumeWeak ? "Y" : "N")} vol={current.Volume:N0} vma5={volumeMa5:N0}; inverseBullTurn={(haBullTurn ? "Y" : "N")}; inverseBullHold2={(haBullHold2 ? "Y" : "N")}",
                            maxHigh,
                            entryPrice,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            stopPrice,
                            stopTime));
                    return true;
                }

                if (string.IsNullOrWhiteSpace(targetExitDate) ||
                    string.CompareOrdinal(ResolveTime(current.DateTime), "110000") < 0)
                {
                    continue;
                }

                result = new EntryExitResult(
                    entryTime,
                    entryPrice,
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    stopPrice,
                    Math.Max(0, holdingMinutes),
                    entryReason,
                    BuildOneMinuteExitReason(
                        $"hold to next trading day 11:00 close; last inverseBullTurn={(haBullTurn ? "Y" : "N")}; last inverseBullHold2={(haBullHold2 ? "Y" : "N")}",
                        maxHigh,
                        entryPrice,
                        firstVerdict,
                        target3Price,
                        target3Time,
                        stopPrice,
                        stopTime));
                return true;
            }

            return false;
        }

        private bool TryResolveFiveMinuteExitFromEntry(
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> fiveBars,
            string entryTime,
            long entryPrice,
            string entryReason,
            out EntryExitResult result)
        {
            result = default;
            int entryIndex = FindTimeIndex(fiveBars, entryTime);
            if (entryIndex < 0 || entryIndex + 1 >= fiveBars.Count)
                return false;

            string entryDate = ResolveDate(entryTime);
            long maxHigh = entryPrice;
            long minLow = entryPrice;
            long target3Price = entryPrice > 0
                ? (long)Math.Ceiling(entryPrice * 1.03m)
                : 0;
            long structuralStopPrice = _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                ? baseState.Low
                : _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                    ? (long)Math.Round(baseState.Ma200, MidpointRounding.AwayFromZero)
                    : baseState.StopPrice;
            string structuralStopLabel = _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                ? "signalLow5mCloseStop"
                : _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                    ? "ma2005mCloseStop"
                    : "prevCloseStop";
            string firstVerdict = string.Empty;
            string target3Time = string.Empty;
            string stopTime = string.Empty;
            string targetExitDate = string.Empty;

            for (int i = entryIndex; i < fiveBars.Count; i++)
            {
                BacktestMinuteBar current = fiveBars[i];
                string currentDate = ResolveDate(current.DateTime);
                if (string.IsNullOrWhiteSpace(currentDate))
                    continue;

                if (!string.Equals(entryDate, currentDate, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(targetExitDate))
                {
                    targetExitDate = currentDate;
                }

                if (!string.IsNullOrWhiteSpace(targetExitDate) &&
                    !string.Equals(currentDate, targetExitDate, StringComparison.Ordinal))
                {
                    break;
                }

                int holdingMinutes = ResolveHoldingMinutes(entryTime, current.DateTime, i - entryIndex);
                if (holdingMinutes < 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                if (string.IsNullOrWhiteSpace(firstVerdict))
                {
                    bool target3Hit = target3Price > 0 && current.High >= target3Price;
                    long currentMa200Stop = ResolveMaAt(fiveBars, i, 200);
                    bool structuralStopHit = structuralStopPrice > 0 &&
                        (_exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                            ? currentMa200Stop > 0 && current.Close < currentMa200Stop
                            : _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                            ? current.Close < structuralStopPrice
                            : current.Low < structuralStopPrice);

                    if (target3Hit && structuralStopHit)
                    {
                        firstVerdict = "BOTH_SAME_BAR";
                        target3Time = current.DateTime;
                        stopTime = current.DateTime;
                    }
                    else if (target3Hit)
                    {
                        firstVerdict = "TARGET_3_FIRST";
                        target3Time = current.DateTime;
                    }
                    else if (structuralStopHit)
                    {
                        firstVerdict = _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                            ? "MA200_5M_CLOSE_STOP_FIRST"
                            : _exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop
                            ? "SIGNAL_LOW_5M_CLOSE_STOP_FIRST"
                            : "PREV_CLOSE_STOP_FIRST";
                        stopTime = current.DateTime;
                    }
                }

                long currentMa200 = _exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop
                    ? ResolveMaAt(fiveBars, i, 200)
                    : 0;

                if (_exitMode == FiveMinuteExitMode.Ma200FiveMinuteCloseStop &&
                    currentMa200 > 0 &&
                    current.Close < currentMa200)
                {
                    result = new EntryExitResult(
                        entryTime,
                        entryPrice,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        currentMa200,
                        Math.Max(0, holdingMinutes),
                        entryReason,
                        BuildFiveMinuteExitReason(
                            "5m close below MA200",
                            maxHigh,
                            entryPrice,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            currentMa200,
                            stopTime,
                            structuralStopLabel,
                            default,
                            default));
                    return true;
                }

                if (_exitMode == FiveMinuteExitMode.SignalLowFiveMinuteCloseStop &&
                    structuralStopPrice > 0 &&
                    current.Close < structuralStopPrice)
                {
                    result = new EntryExitResult(
                        entryTime,
                        entryPrice,
                        current.DateTime,
                        current.Close,
                        maxHigh,
                        minLow,
                        structuralStopPrice,
                        Math.Max(0, holdingMinutes),
                        entryReason,
                        BuildFiveMinuteExitReason(
                            "5m close below signal candle low",
                            maxHigh,
                            entryPrice,
                            firstVerdict,
                            target3Price,
                            target3Time,
                            structuralStopPrice,
                            stopTime,
                            structuralStopLabel,
                            default,
                            default));
                    return true;
                }

                if (string.IsNullOrWhiteSpace(targetExitDate) ||
                    string.CompareOrdinal(ResolveTime(current.DateTime), "110000") < 0)
                {
                    continue;
                }

                result = new EntryExitResult(
                    entryTime,
                    entryPrice,
                    current.DateTime,
                    current.Close,
                    maxHigh,
                    minLow,
                    structuralStopPrice,
                    Math.Max(0, holdingMinutes),
                    entryReason,
                    BuildFiveMinuteExitReason(
                        "hold to next trading day 11:00 close",
                        maxHigh,
                        entryPrice,
                        firstVerdict,
                        target3Price,
                        target3Time,
                        structuralStopPrice,
                        stopTime,
                        structuralStopLabel,
                        default,
                        default));
                return true;
            }

            return false;
        }

        private static string BuildFiveMinuteExitReason(
            string exitLabel,
            long maxHigh,
            long entryPrice,
            string firstVerdict,
            long target3Price,
            string target3Time,
            long stopPrice,
            string stopTime,
            string stopLabel,
            LinearRegressionExitState regressionExit,
            Ma240DeviationUpperExitState ma240UpperExit)
        {
            decimal mfe = entryPrice > 0
                ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m
                : 0m;
            string verdict = string.IsNullOrWhiteSpace(firstVerdict)
                ? "NO_TARGET_OR_STOP"
                : firstVerdict;
            string regression = regressionExit.IsReady
                ? $"; lr fast {(regressionExit.FastCrossDown ? "Y" : "N")} slow {(regressionExit.SlowCrossDown ? "Y" : "N")} VL {regressionExit.FastLine:N0} VL1 {regressionExit.SlowLine:N0}"
                : string.Empty;
            string ma240 = ma240UpperExit.IsReady
                ? $"; ma240Upper D {ma240UpperExit.UpperLine:N0} MA240 {ma240UpperExit.Ma240:N0} posAvg {ma240UpperExit.PositiveDeviationAverage:N0} posStd {ma240UpperExit.PositiveDeviationStdDev:N0}"
                : string.Empty;

            return $"{exitLabel}; maxHigh {maxHigh:N0}; high move {mfe:0.##}%; verdict {verdict}; target3 {target3Price:N0} {target3Time}; {stopLabel} {stopPrice:N0} {stopTime}{regression}{ma240}";
        }

        private static string BuildOneMinuteExitReason(
            string exitLabel,
            long maxHigh,
            long entryPrice,
            string firstVerdict,
            long target3Price,
            string target3Time,
            long stopPrice,
            string stopTime)
        {
            decimal mfe = entryPrice > 0
                ? (maxHigh - entryPrice) / (decimal)entryPrice * 100m
                : 0m;
            string verdict = string.IsNullOrWhiteSpace(firstVerdict)
                ? "NO_TARGET_OR_STOP"
                : firstVerdict;

            return $"{exitLabel}; maxHigh {maxHigh:N0}; high move {mfe:0.##}%; verdict {verdict}; target3 {target3Price:N0} {target3Time}; prevLowStop {stopPrice:N0} {stopTime}";
        }

        private static bool TryResolveSignalExit(
            BacktestMinuteBar entryBar,
            BaseState baseState,
            IReadOnlyList<BacktestMinuteBar> oneBars,
            int entryIndex,
            IReadOnlyDictionary<string, MaState> oneStateByTime,
            IReadOnlyDictionary<string, MaState> fifteenStateByTime,
            out ExitResult result)
        {
            result = default;
            string entryDate = ResolveDate(entryBar.DateTime);
            long maxHigh = entryBar.High;
            long minLow = entryBar.Low;

            for (int i = entryIndex + 1; i < oneBars.Count; i++)
            {
                BacktestMinuteBar current = oneBars[i];
                if (!string.Equals(entryDate, ResolveDate(current.DateTime), StringComparison.Ordinal))
                    break;

                int holdingMinutes = ResolveHoldingMinutes(entryBar.DateTime, current.DateTime, i - entryIndex);
                if (holdingMinutes <= 0)
                    continue;

                maxHigh = Math.Max(maxHigh, current.High);
                minLow = Math.Min(minLow, current.Low);

                if (string.CompareOrdinal(ResolveTime(current.DateTime), "152000") < 0)
                    continue;

                result = new ExitResult(current.DateTime, current.Close, maxHigh, minLow, holdingMinutes, "hold to 15:20 close");
                return true;
            }

            return false;
        }

        private static Dictionary<string, MaState> BuildMaStateMap(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new Dictionary<string, MaState>(StringComparer.Ordinal);
            int resolved = Math.Max(2, period);
            for (int i = resolved - 1; i < bars.Count; i++)
            {
                BacktestMinuteBar current = bars[i];
                decimal ma = bars.Skip(i - resolved + 1).Take(resolved).Average(bar => (decimal)bar.Close);
                long previousClose = i > 0 ? bars[i - 1].Close : current.Close;
                decimal previousMa = i > resolved - 1
                    ? bars.Skip(i - resolved).Take(resolved).Average(bar => (decimal)bar.Close)
                    : ma;

                result[current.DateTime] = new MaState(current.DateTime, current.Close, ma, previousClose, previousMa);
            }

            return result;
        }

        private static long ResolveMaAt(IReadOnlyList<BacktestMinuteBar> bars, int currentIndex, int period)
        {
            int resolved = Math.Max(2, period);
            if (currentIndex < resolved - 1 || currentIndex >= bars.Count)
                return 0;

            decimal average = bars
                .Skip(currentIndex - resolved + 1)
                .Take(resolved)
                .Average(bar => (decimal)bar.Close);
            return (long)Math.Round(average, MidpointRounding.AwayFromZero);
        }

        private static bool TryGetLatestMaState(
            IReadOnlyDictionary<string, MaState> states,
            string time,
            out MaState state)
        {
            state = default;
            string? key = states.Keys
                .Where(item => string.CompareOrdinal(item, time) <= 0)
                .OrderBy(item => item)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            state = states[key];
            return state.Ma > 0;
        }

        private static bool TryEvaluateLinearRegressionExit(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            out LinearRegressionExitState state)
        {
            state = default;
            const int fastPeriod = 50;
            const int slowPeriod = 100;
            if (currentIndex < Math.Max(fastPeriod * 2, slowPeriod * 2) || currentIndex <= 0)
                return false;

            if (!TryResolveRegressionLine(bars, currentIndex, fastPeriod, out double fastLine) ||
                !TryResolveRegressionLine(bars, currentIndex - 1, fastPeriod, out double previousFastLine) ||
                !TryResolveRegressionLine(bars, currentIndex, slowPeriod, out double slowLine) ||
                !TryResolveRegressionLine(bars, currentIndex - 1, slowPeriod, out double previousSlowLine))
            {
                return false;
            }

            double currentClose = bars[currentIndex].Close;
            double previousClose = bars[currentIndex - 1].Close;
            bool fastCrossDown = previousClose >= previousFastLine && currentClose < fastLine;
            bool slowCrossDown = previousClose >= previousSlowLine && currentClose < slowLine;
            state = new LinearRegressionExitState(
                true,
                fastCrossDown,
                slowCrossDown,
                fastCrossDown || slowCrossDown,
                fastLine,
                slowLine);
            return true;
        }

        private static bool TryEvaluateMa240DeviationUpperExit(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            out Ma240DeviationUpperExitState state)
        {
            state = default;
            if (currentIndex <= 0)
                return false;

            if (!TryResolveMa240DeviationUpperLine(bars, currentIndex, out Ma240DeviationUpperLine currentLine) ||
                !TryResolveMa240DeviationUpperLine(bars, currentIndex - 1, out Ma240DeviationUpperLine previousLine))
            {
                return false;
            }

            long currentClose = bars[currentIndex].Close;
            long previousClose = bars[currentIndex - 1].Close;
            bool crossUp = previousClose <= previousLine.UpperLine && currentClose > currentLine.UpperLine;
            state = new Ma240DeviationUpperExitState(
                true,
                crossUp,
                currentLine.Ma240,
                currentLine.UpperLine,
                currentLine.PositiveDeviationAverage,
                currentLine.PositiveDeviationStdDev);
            return true;
        }

        private static bool TryEvaluateMa240DeviationLowerRecovery(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            out Ma240DeviationBandState state)
        {
            state = default;
            if (currentIndex <= 0)
                return false;

            if (!TryResolveMa240DeviationBandLine(bars, currentIndex, out Ma240DeviationBandLine currentLine) ||
                !TryResolveMa240DeviationBandLine(bars, currentIndex - 1, out Ma240DeviationBandLine previousLine))
            {
                return false;
            }

            long currentClose = bars[currentIndex].Close;
            long previousClose = bars[currentIndex - 1].Close;
            bool crossUp = previousClose <= previousLine.LowerLine && currentClose > currentLine.LowerLine;
            state = new Ma240DeviationBandState(
                true,
                crossUp,
                false,
                currentLine.Ma240,
                currentLine.LowerLine,
                currentLine.UpperLine,
                currentLine.NegativeDeviationAverage,
                currentLine.NegativeDeviationStdDev,
                currentLine.PositiveDeviationAverage,
                currentLine.PositiveDeviationStdDev);
            return true;
        }

        private static bool TryEvaluateMa240DeviationUpperExtension(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            out Ma240DeviationBandState state)
        {
            state = default;
            if (currentIndex <= 0)
                return false;

            if (!TryResolveMa240DeviationBandLine(bars, currentIndex, out Ma240DeviationBandLine currentLine) ||
                !TryResolveMa240DeviationBandLine(bars, currentIndex - 1, out Ma240DeviationBandLine previousLine))
            {
                return false;
            }

            long currentClose = bars[currentIndex].Close;
            long previousClose = bars[currentIndex - 1].Close;
            bool crossUp = previousClose <= previousLine.UpperLine && currentClose > currentLine.UpperLine;
            state = new Ma240DeviationBandState(
                true,
                false,
                crossUp,
                currentLine.Ma240,
                currentLine.LowerLine,
                currentLine.UpperLine,
                currentLine.NegativeDeviationAverage,
                currentLine.NegativeDeviationStdDev,
                currentLine.PositiveDeviationAverage,
                currentLine.PositiveDeviationStdDev);
            return true;
        }

        private static bool TryResolveMa240DeviationUpperLine(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            out Ma240DeviationUpperLine line)
        {
            line = default;
            const int period = 240;
            if (currentIndex - (period * 2) + 2 < 0)
                return false;

            decimal ma240 = bars.Skip(currentIndex - period + 1).Take(period).Average(bar => (decimal)bar.Close);
            List<decimal> positiveDeviations = [];
            for (int i = currentIndex - period + 1; i <= currentIndex; i++)
            {
                decimal rollingMa = bars.Skip(i - period + 1).Take(period).Average(bar => (decimal)bar.Close);
                decimal deviation = bars[i].Close - rollingMa;
                if (deviation > 0)
                    positiveDeviations.Add(deviation);
            }

            if (positiveDeviations.Count < 2)
                return false;

            decimal average = positiveDeviations.Average();
            decimal variance = positiveDeviations.Average(value => (value - average) * (value - average));
            decimal stdDev = (decimal)Math.Sqrt((double)variance);
            decimal upper = ma240 + average + (2m * stdDev);
            line = new Ma240DeviationUpperLine(ma240, upper, average, stdDev);
            return true;
        }

        private static bool TryResolveMa240DeviationBandLine(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            out Ma240DeviationBandLine line)
        {
            line = default;
            const int period = 240;
            if (currentIndex - (period * 2) + 2 < 0)
                return false;

            decimal ma240 = bars.Skip(currentIndex - period + 1).Take(period).Average(bar => (decimal)bar.Close);
            List<decimal> positiveDeviations = [];
            List<decimal> negativeDeviations = [];
            for (int i = currentIndex - period + 1; i <= currentIndex; i++)
            {
                decimal rollingMa = bars.Skip(i - period + 1).Take(period).Average(bar => (decimal)bar.Close);
                decimal deviation = bars[i].Close - rollingMa;
                if (deviation > 0)
                    positiveDeviations.Add(deviation);
                else if (deviation < 0)
                    negativeDeviations.Add(deviation);
            }

            if (positiveDeviations.Count < 2 || negativeDeviations.Count < 2)
                return false;

            decimal positiveAverage = positiveDeviations.Average();
            decimal positiveVariance = positiveDeviations.Average(value => (value - positiveAverage) * (value - positiveAverage));
            decimal positiveStdDev = (decimal)Math.Sqrt((double)positiveVariance);
            decimal negativeAverage = negativeDeviations.Average();
            decimal negativeVariance = negativeDeviations.Average(value => (value - negativeAverage) * (value - negativeAverage));
            decimal negativeStdDev = (decimal)Math.Sqrt((double)negativeVariance);
            decimal lower = ma240 + negativeAverage - (2m * negativeStdDev);
            decimal upper = ma240 + positiveAverage + (2m * positiveStdDev);
            line = new Ma240DeviationBandLine(
                ma240,
                lower,
                upper,
                negativeAverage,
                negativeStdDev,
                positiveAverage,
                positiveStdDev);
            return true;
        }

        private static bool TryResolveRegressionLine(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            int period,
            out double line)
        {
            line = 0;
            if (!TryResolveRegressionEndpointFromClose(bars, currentIndex, period, out double first) ||
                !TryResolveRegressionEndpointFromRegression(bars, currentIndex, period, out double second))
            {
                return false;
            }

            line = first + (first - second);
            return true;
        }

        private static bool TryResolveRegressionEndpointFromClose(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            int period,
            out double endpoint)
        {
            endpoint = 0;
            if (currentIndex - period + 1 < 0)
                return false;

            double[] values = new double[period];
            for (int i = 0; i < period; i++)
                values[i] = bars[currentIndex - period + 1 + i].Close;

            endpoint = CalculateRegressionEndpoint(values);
            return true;
        }

        private static bool TryResolveRegressionEndpointFromRegression(
            IReadOnlyList<BacktestMinuteBar> bars,
            int currentIndex,
            int period,
            out double endpoint)
        {
            endpoint = 0;
            if (currentIndex - (period * 2) + 2 < 0)
                return false;

            double[] values = new double[period];
            for (int i = 0; i < period; i++)
            {
                int sourceIndex = currentIndex - period + 1 + i;
                if (!TryResolveRegressionEndpointFromClose(bars, sourceIndex, period, out double firstEndpoint))
                    return false;

                values[i] = firstEndpoint;
            }

            endpoint = CalculateRegressionEndpoint(values);
            return true;
        }

        private static double CalculateRegressionEndpoint(IReadOnlyList<double> values)
        {
            int count = values.Count;
            double sumX = 0;
            double sumY = 0;
            double sumXY = 0;
            double sumXX = 0;

            for (int index = 0; index < count; index++)
            {
                double x = index;
                double y = values[index];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            double denominator = count * sumXX - sumX * sumX;
            if (Math.Abs(denominator) < double.Epsilon)
                return values[^1];

            double slope = (count * sumXY - sumX * sumY) / denominator;
            double intercept = (sumY - slope * sumX) / count;
            return intercept + slope * (count - 1);
        }

        private static BacktestTradeRow BuildTradeRow(string runId, StockMarketKey stock, EntryExitResult trade, string strategyCode, string exitRuleCode)
        {
            long riskWon = trade.EntryPrice - trade.StopPrice;
            decimal profitRate = trade.EntryPrice > 0
                ? (trade.ExitPrice - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal mae = trade.EntryPrice > 0
                ? (trade.MinLow - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal mfe = trade.EntryPrice > 0
                ? (trade.MaxHigh - trade.EntryPrice) / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal riskRate = trade.EntryPrice > 0 && riskWon > 0
                ? riskWon / (decimal)trade.EntryPrice * 100m
                : 0m;
            decimal maxR = riskWon > 0 ? (trade.MaxHigh - trade.EntryPrice) / (decimal)riskWon : 0m;
            decimal minR = riskWon > 0 ? (trade.MinLow - trade.EntryPrice) / (decimal)riskWon : 0m;

            return new BacktestTradeRow
            {
                RunId = runId,
                StrategyCode = strategyCode,
                ExitRuleCode = exitRuleCode,
                Code = stock.Code,
                Market = stock.Market,
                EntryTime = trade.EntryTime,
                ExitTime = trade.ExitTime,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                MaxHigh = trade.MaxHigh,
                MinLow = trade.MinLow,
                StopPrice = trade.StopPrice,
                Quantity = 1,
                ProfitRate = profitRate,
                ProfitAmount = trade.ExitPrice - trade.EntryPrice,
                Mae = mae,
                Mfe = mfe,
                RiskRate = riskRate,
                MaxR = maxR,
                MinR = minR,
                HoldingMinutes = trade.HoldingMinutes,
                EntryReason = trade.EntryReason,
                ExitReason = trade.ExitReason
            };
        }

        private RangeFactorDiagnosticRow BuildRangeFactorDiagnostic(
            StockMarketKey stock,
            BaseState baseState,
            BacktestTradeRow trade)
        {
            decimal ma200GapRate = CalculateRate(baseState.Close, baseState.Ma200);
            decimal dailyMa5To60Rate = CalculateRate(baseState.DailyMa5, baseState.DailyMa60);
            decimal triggerToPreviousHighRate = CalculateRate(baseState.Close, baseState.PreviousDailyHigh);
            decimal bodyRate = CalculateBodyRate(baseState.Open, baseState.Close);
            decimal upperWickRate = CalculateUpperWickRate(baseState.Open, baseState.High, baseState.Close);
            decimal lowerWickRate = CalculateLowerWickRate(baseState.Open, baseState.Low, baseState.Close);
            decimal closeLocationRate = CalculateCloseLocationRate(baseState.Low, baseState.High, baseState.Close);
            string signalTime = ResolveTime(baseState.Time);
            string signalHour = signalTime.Length >= 2 ? signalTime[..2] : string.Empty;

            return new RangeFactorDiagnosticRow(
                _rangeFactor,
                stock.Code,
                stock.Market,
                ResolveDate(baseState.Time),
                signalHour,
                baseState.Time,
                trade.EntryTime,
                trade.ExitTime,
                baseState.DayOpen,
                baseState.PreviousDailyHigh,
                baseState.PreviousDailyLow,
                baseState.TriggerLine,
                baseState.Rsi2,
                baseState.PriceMa5,
                baseState.PriceMa20,
                baseState.PriceMa60,
                baseState.Ma200,
                ma200GapRate,
                baseState.Volume,
                baseState.VolumeMa5,
                baseState.VolumeMa20,
                baseState.VolumeMa60,
                baseState.TradingValueMa5,
                baseState.TradingValueMa20,
                baseState.TradingValueMa60,
                bodyRate,
                upperWickRate,
                lowerWickRate,
                closeLocationRate,
                baseState.DailyMa5,
                baseState.DailyMa20,
                baseState.DailyMa60,
                dailyMa5To60Rate,
                baseState.DailyTradingValue,
                baseState.TradingValue,
                triggerToPreviousHighRate,
                ResolveDailyValueBucket(baseState.DailyTradingValue),
                ResolveMa200PositionBucket(ma200GapRate),
                ResolveVolumeExpansionBucket(baseState.Volume, baseState.VolumeMa5, baseState.VolumeMa20, baseState.VolumeMa60),
                ResolveTradingValueExpansionBucket(baseState.TradingValue, baseState.TradingValueMa5, baseState.TradingValueMa20, baseState.TradingValueMa60),
                ResolveCandleStructureBucket(baseState.Open, baseState.Close, bodyRate, upperWickRate, closeLocationRate),
                ResolvePriceMaStructureBucket(baseState.Close, baseState.PriceMa5, baseState.PriceMa20, baseState.PriceMa60),
                ResolveDailyMaSpreadBucket(dailyMa5To60Rate, baseState.DailyMa5, baseState.DailyMa20, baseState.DailyMa60),
                trade.ProfitRate,
                trade.Mae,
                trade.Mfe,
                ResolveOutcomeBucket(trade),
                trade.ExitReason);
        }

        private static decimal CalculateRate(long current, decimal basis) =>
            basis > 0 ? (current - basis) / basis * 100m : 0m;

        private static decimal CalculateRate(decimal current, decimal basis) =>
            basis > 0 ? (current - basis) / basis * 100m : 0m;

        private static decimal CalculateRate(long current, long basis) =>
            basis > 0 ? (current - basis) / (decimal)basis * 100m : 0m;

        private static decimal CalculateAverageClose(IReadOnlyList<BacktestMinuteBar> bars, int index, int period) =>
            index >= period - 1
                ? bars.Skip(index - period + 1).Take(period).Average(bar => (decimal)bar.Close)
                : 0m;

        private static decimal CalculateAverageVolume(IReadOnlyList<BacktestMinuteBar> bars, int index, int period) =>
            index >= period - 1
                ? bars.Skip(index - period + 1).Take(period).Average(bar => (decimal)bar.Volume)
                : 0m;

        private static decimal CalculateAverageTradingValue(IReadOnlyList<BacktestMinuteBar> bars, int index, int period) =>
            index >= period - 1
                ? bars.Skip(index - period + 1).Take(period).Average(bar => (decimal)bar.TradingValue)
                : 0m;

        private static decimal CalculateBodyRate(long open, long close) =>
            open > 0 ? Math.Abs(close - open) / (decimal)open * 100m : 0m;

        private static decimal CalculateUpperWickRate(long open, long high, long close)
        {
            long bodyHigh = Math.Max(open, close);
            return open > 0 && high > bodyHigh ? (high - bodyHigh) / (decimal)open * 100m : 0m;
        }

        private static decimal CalculateLowerWickRate(long open, long low, long close)
        {
            long bodyLow = Math.Min(open, close);
            return open > 0 && low < bodyLow ? (bodyLow - low) / (decimal)open * 100m : 0m;
        }

        private static decimal CalculateCloseLocationRate(long low, long high, long close) =>
            high > low ? (close - low) / (decimal)(high - low) * 100m : 50m;

        private static string ResolveDailyValueBucket(long tradingValue) =>
            tradingValue switch
            {
                >= 300_000_000_000 => "3000eok+",
                >= 100_000_000_000 => "1000-3000eok",
                >= 50_000_000_000 => "500-1000eok",
                _ => "under500eok"
            };

        private static string ResolveMa200PositionBucket(decimal ma200GapRate) =>
            ma200GapRate switch
            {
                0m => "ma200-none-or-touch",
                < 0m => "below-ma200",
                <= 2m => "near-ma200-0-2",
                <= 5m => "above-ma200-2-5",
                _ => "above-ma200-5+"
            };

        private static string ResolveVolumeExpansionBucket(long volume, decimal ma5, decimal ma20, decimal ma60)
        {
            if (volume <= 0 || ma5 <= 0 || ma20 <= 0 || ma60 <= 0)
                return "vol-missing";

            if (volume >= ma60 * 2m && ma5 > ma20 && ma20 >= ma60)
                return "vol-strong-2x60-ma-aligned";

            if (volume >= ma20 * 1.5m && ma5 > ma20)
                return "vol-expanding-1p5x20";

            if (volume >= ma5)
                return "vol-above-ma5";

            return "vol-weak";
        }

        private static string ResolveTradingValueExpansionBucket(long tradingValue, decimal ma5, decimal ma20, decimal ma60)
        {
            if (tradingValue <= 0 || ma5 <= 0 || ma20 <= 0 || ma60 <= 0)
                return "value-missing";

            if (tradingValue >= ma60 * 2m && ma5 > ma20 && ma20 >= ma60)
                return "value-strong-2x60-ma-aligned";

            if (tradingValue >= ma20 * 1.5m && ma5 > ma20)
                return "value-expanding-1p5x20";

            if (tradingValue >= ma5)
                return "value-above-ma5";

            return "value-weak";
        }

        private static string ResolveCandleStructureBucket(long open, long close, decimal bodyRate, decimal upperWickRate, decimal closeLocationRate)
        {
            if (open <= 0 || close <= 0)
                return "candle-missing";

            if (close > open && closeLocationRate >= 70m && upperWickRate <= bodyRate)
                return "bull-close-high";

            if (close > open && upperWickRate > bodyRate)
                return "bull-upper-wick-heavy";

            if (close > open)
                return "bull";

            if (close < open && closeLocationRate <= 30m)
                return "bear-close-low";

            if (close < open)
                return "bear";

            return "doji";
        }

        private static string ResolvePriceMaStructureBucket(long close, decimal ma5, decimal ma20, decimal ma60)
        {
            if (close <= 0 || ma5 <= 0 || ma20 <= 0 || ma60 <= 0)
                return "price-ma-missing";

            if (close > ma5 && ma5 > ma20 && ma20 > ma60)
                return "price-above-5m-ma-aligned";

            if (close > ma60 && ma5 > ma20)
                return "price-above-ma60-short-up";

            if (close > ma60)
                return "price-above-ma60";

            if (close < ma60 && ma5 < ma20)
                return "price-below-ma60-short-down";

            return "price-below-ma60";
        }

        private static string ResolveDailyMaSpreadBucket(decimal dailyMa5To60Rate, decimal ma5, decimal ma20, decimal ma60)
        {
            if (ma5 <= 0 || ma20 <= 0 || ma60 <= 0)
                return "daily-ma-missing";

            if (ma5 < ma20 || ma5 < ma60)
                return "daily-ma-not-aligned";

            return dailyMa5To60Rate switch
            {
                <= 3m => "daily-ma-tight-0-3",
                <= 8m => "daily-ma-spread-3-8",
                _ => "daily-ma-hot-8+"
            };
        }

        private static string ResolveOutcomeBucket(BacktestTradeRow trade)
        {
            if (trade.Mfe >= 5m && trade.Mae > -3m)
                return "clean-mfe5";

            if (trade.Mfe >= 3m && trade.Mae > -3m)
                return "clean-mfe3";

            if (trade.Mfe >= 3m)
                return "mfe3-with-drawdown";

            if (trade.ProfitRate > 0m)
                return "small-win";

            return "failed";
        }

        private static void SaveRangeFactorDiagnostics(string outputDirectory, IReadOnlyList<RangeFactorDiagnosticRow> rows)
        {
            if (rows.Count == 0)
                return;

            StringBuilder builder = new();
            AppendCsvLine(
                builder,
                "Factor",
                "Code",
                "Market",
                "SignalDate",
                "SignalHour",
                "BaseTime",
                "EntryTime",
                "ExitTime",
                "DayOpen",
                "PreviousHigh",
                "PreviousLow",
                "TriggerLine",
                "Rsi2",
                "PriceMa5",
                "PriceMa20",
                "PriceMa60",
                "Ma200",
                "Ma200GapRate",
                "Volume",
                "VolumeMa5",
                "VolumeMa20",
                "VolumeMa60",
                "TradingValueMa5",
                "TradingValueMa20",
                "TradingValueMa60",
                "BodyRate",
                "UpperWickRate",
                "LowerWickRate",
                "CloseLocationRate",
                "DailyMa5",
                "DailyMa20",
                "DailyMa60",
                "DailyMa5To60Rate",
                "DailyTradingValue",
                "BaseTradingValue",
                "TriggerToPreviousHighRate",
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
                "ExitReason");

            foreach (RangeFactorDiagnosticRow row in rows)
            {
                AppendCsvLine(
                    builder,
                    row.Factor.ToString("0.00", CultureInfo.InvariantCulture),
                    row.Code,
                    row.Market,
                    row.SignalDate,
                    row.SignalHour,
                    row.BaseTime,
                    row.EntryTime,
                    row.ExitTime,
                    row.DayOpen.ToString(CultureInfo.InvariantCulture),
                    row.PreviousHigh.ToString(CultureInfo.InvariantCulture),
                    row.PreviousLow.ToString(CultureInfo.InvariantCulture),
                    row.TriggerLine.ToString("0.####", CultureInfo.InvariantCulture),
                    row.Rsi2.ToString("0.####", CultureInfo.InvariantCulture),
                    row.PriceMa5.ToString("0.####", CultureInfo.InvariantCulture),
                    row.PriceMa20.ToString("0.####", CultureInfo.InvariantCulture),
                    row.PriceMa60.ToString("0.####", CultureInfo.InvariantCulture),
                    row.Ma200.ToString("0.####", CultureInfo.InvariantCulture),
                    row.Ma200GapRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.Volume.ToString(CultureInfo.InvariantCulture),
                    row.VolumeMa5.ToString("0.####", CultureInfo.InvariantCulture),
                    row.VolumeMa20.ToString("0.####", CultureInfo.InvariantCulture),
                    row.VolumeMa60.ToString("0.####", CultureInfo.InvariantCulture),
                    row.TradingValueMa5.ToString("0.####", CultureInfo.InvariantCulture),
                    row.TradingValueMa20.ToString("0.####", CultureInfo.InvariantCulture),
                    row.TradingValueMa60.ToString("0.####", CultureInfo.InvariantCulture),
                    row.BodyRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.UpperWickRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.LowerWickRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.CloseLocationRate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.DailyMa5.ToString("0.####", CultureInfo.InvariantCulture),
                    row.DailyMa20.ToString("0.####", CultureInfo.InvariantCulture),
                    row.DailyMa60.ToString("0.####", CultureInfo.InvariantCulture),
                    row.DailyMa5To60Rate.ToString("0.####", CultureInfo.InvariantCulture),
                    row.DailyTradingValue.ToString(CultureInfo.InvariantCulture),
                    row.BaseTradingValue.ToString(CultureInfo.InvariantCulture),
                    row.TriggerToPreviousHighRate.ToString("0.####", CultureInfo.InvariantCulture),
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
                    row.ExitReason);
            }

            File.WriteAllText(Path.Combine(outputDirectory, "range_factor_trade_diagnostics.csv"), builder.ToString(), Encoding.UTF8);
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

        private static List<BacktestSignalRow> FilterSignals(
            IEnumerable<BacktestSignalRow> signals,
            string strategyCode,
            string? market = null) =>
            [.. signals.Where(item =>
                string.Equals(item.StrategyCode, strategyCode, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(market) || string.Equals(item.Market, market, StringComparison.Ordinal)))];

        private static List<BacktestTradeRow> FilterTrades(
            IEnumerable<BacktestTradeRow> trades,
            string strategyCode,
            string? market = null) =>
            [.. trades.Where(item =>
                string.Equals(item.StrategyCode, strategyCode, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(market) || string.Equals(item.Market, market, StringComparison.Ordinal)))];

        private static BacktestRunSummary BuildSummary(
            string runId,
            string strategyCode,
            string exitRuleCode,
            string market,
            IReadOnlyList<BacktestSignalRow> signals,
            IReadOnlyList<BacktestTradeRow> trades)
        {
            int wins = trades.Count(trade => trade.ProfitRate > 0);
            List<decimal> profits = [.. trades.Where(trade => trade.ProfitRate > 0).Select(trade => trade.ProfitRate)];
            List<decimal> losses = [.. trades.Where(trade => trade.ProfitRate <= 0).Select(trade => trade.ProfitRate)];

            return new BacktestRunSummary
            {
                RunId = runId,
                Market = market,
                StrategyCode = strategyCode,
                ExitRuleCode = exitRuleCode,
                SignalCount = signals.Count,
                TradeCount = trades.Count,
                WinRate = trades.Count > 0 ? wins / (decimal)trades.Count * 100m : 0m,
                AvgProfit = profits.Count > 0 ? profits.Average() : 0m,
                AvgLoss = losses.Count > 0 ? losses.Average() : 0m,
                Expectancy = trades.Count > 0 ? trades.Average(trade => trade.ProfitRate) : 0m,
                TotalProfit = trades.Sum(trade => trade.ProfitAmount),
                MaxDrawdown = trades.Count > 0 ? trades.Min(trade => trade.Mae) : 0m,
                MAE = trades.Count > 0 ? trades.Average(trade => trade.Mae) : 0m,
                MFE = trades.Count > 0 ? trades.Average(trade => trade.Mfe) : 0m,
                AvgHoldingMinutes = trades.Count > 0 ? trades.Average(trade => (decimal)trade.HoldingMinutes) : 0m,
                ConsecutiveLosses = ResolveMaxConsecutiveLosses(trades),
                FeeAdjustedProfit = trades.Sum(trade => trade.ProfitAmount),
                SlippageAdjustedProfit = trades.Sum(trade => trade.ProfitAmount)
            };
        }

        private void SaveCharts(string outputDirectory, IEnumerable<ChartRequest> requests)
        {
            List<ChartRequest> materializedRequests = [.. requests];
            string chartDirectory = Path.Combine(outputDirectory, "charts");
            Directory.CreateDirectory(chartDirectory);
            foreach (ChartRequest request in materializedRequests)
            {
                List<BacktestMinuteBar> fiveBars = LoadMinuteBars(request.Stock, 5);
                int baseIndex = FindTimeIndex(fiveBars, request.BaseTime);
                if (baseIndex < 0)
                    continue;

                int start = Math.Max(0, baseIndex - 360);
                int end = Math.Min(fiveBars.Count - 1, baseIndex + 120);
                List<BacktestMinuteBar> window = [.. fiveBars.Skip(start).Take(end - start + 1)];
                if (window.Count < 10)
                    continue;

                string name = $"{request.Stock.Code}_{request.Stock.Market}_{request.EntryTime}.png";
                string path = Path.Combine(chartDirectory, name);
                RenderChart(path, request.Stock, window, request.BaseTime, request.EntryTime, request.ExitTime, request.EntryPrice, request.ExitPrice);
            }

            SaveThirtyMinuteStructureCharts(outputDirectory, materializedRequests);
        }

        private void SaveThirtyMinuteStructureCharts(string outputDirectory, IEnumerable<ChartRequest> requests)
        {
            string structureDirectory = Path.Combine(outputDirectory, "charts_30m_structure");
            Directory.CreateDirectory(structureDirectory);

            var service = new ThirtyMinuteBaselineService(_dataStore);
            List<ThirtyMinuteBaselineSignalChartResult> rows = [];
            foreach (ChartRequest request in requests)
            {
                ThirtyMinuteBaselineSignalChartResult? result = service.SaveSignalChart(
                    structureDirectory,
                    request.Stock.Code,
                    request.Stock.Market,
                    request.EntryTime);
                if (result != null)
                    rows.Add(result);
            }

            string csvPath = Path.Combine(outputDirectory, "thirty_minute_signal_structure_summary.csv");
            File.WriteAllText(csvPath, BuildThirtyMinuteSignalChartCsv(rows), Encoding.UTF8);
        }

        private static string BuildThirtyMinuteSignalChartCsv(IEnumerable<ThirtyMinuteBaselineSignalChartResult> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Code,Market,EntryTime,AnchorTime,AnchorClose,NearestSupport,NearestResistance,BreakoutRoomPct,LocationTag,ChartPath");
            foreach (ThirtyMinuteBaselineSignalChartResult row in rows)
            {
                AppendCsvLine(
                    builder,
                    row.Code,
                    row.Market,
                    row.EntryTime,
                    row.AnchorTime,
                    row.AnchorClose.ToString(CultureInfo.InvariantCulture),
                    row.NearestSupport.ToString(CultureInfo.InvariantCulture),
                    row.NearestResistance.ToString(CultureInfo.InvariantCulture),
                    row.BreakoutRoomPct.ToString(CultureInfo.InvariantCulture),
                    row.LocationTag,
                    row.ChartPath);
            }

            return builder.ToString();
        }

        private static void RenderChart(
            string path,
            StockMarketKey stock,
            IReadOnlyList<BacktestMinuteBar> bars,
            string baseTime,
            string entryTime,
            string exitTime,
            long entryPrice,
            long exitPrice)
        {
            const int width = 1200;
            const int height = 720;
            const int left = 60;
            const int right = 40;
            const int top = 48;
            const int priceBottom = 500;
            const int volumeTop = 530;
            const int bottom = 690;

            long high = bars.Max(bar => bar.High);
            long low = bars.Min(bar => bar.Low);
            long maxVolume = Math.Max(1, bars.Max(bar => bar.Volume));
            double xStep = (width - left - right) / (double)Math.Max(1, bars.Count - 1);

            double PriceY(long price)
            {
                if (high == low)
                    return (top + priceBottom) / 2.0;
                return priceBottom - (price - low) / (double)(high - low) * (priceBottom - top);
            }

            double VolumeY(long volume) =>
                bottom - volume / (double)maxVolume * (bottom - volumeTop);

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 14, 14)), null, new Rect(0, 0, width, height));

                var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 45)), 1);
                for (int i = 0; i <= 5; i++)
                {
                    double y = top + i * (priceBottom - top) / 5.0;
                    dc.DrawLine(gridPen, new Point(left, y), new Point(width - right, y));
                }
                dc.DrawLine(gridPen, new Point(left, volumeTop), new Point(width - right, volumeTop));
                dc.DrawLine(gridPen, new Point(left, bottom), new Point(width - right, bottom));

                DrawText(dc, $"{stock.Code} {stock.Market} / 5m PREDAY range half RSI2", 18, 16, 14, Colors.White);
                DrawText(dc, $"BASE {baseTime}   BUY {entryTime}   SELL {exitTime}", 18, 34, 12, Color.FromRgb(180, 190, 205));

                List<double?> ma200 = CalculateMa(bars, 200);
                DrawMa(dc, bars, ma200, left, xStep, PriceY, Color.FromRgb(180, 180, 180));

                if (exitPrice > 0 && exitPrice >= low && exitPrice <= high)
                {
                    double exitY = PriceY(exitPrice);
                    var exitPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 135, 80)), 1.4)
                    {
                        DashStyle = DashStyles.Dash
                    };
                    dc.DrawLine(exitPen, new Point(left, exitY), new Point(width - right, exitY));
                    DrawText(dc, $"SELL {exitPrice:N0}", width - right - 104, exitY + 4, 11, Color.FromRgb(255, 170, 115));
                }

                int entryMarkerIndex = FindLastTimeIndexAtOrBefore(bars, entryTime);
                int exitMarkerIndex = FindLastTimeIndexAtOrBefore(bars, exitTime);

                for (int i = 0; i < bars.Count; i++)
                {
                    BacktestMinuteBar bar = bars[i];
                    double x = left + i * xStep;
                    bool up = bar.Close >= bar.Open;
                    Color color = up ? Color.FromRgb(255, 72, 72) : Color.FromRgb(70, 145, 255);
                    var brush = new SolidColorBrush(color);
                    var pen = new Pen(brush, 1.2);
                    double openY = PriceY(bar.Open);
                    double closeY = PriceY(bar.Close);
                    double highY = PriceY(bar.High);
                    double lowY = PriceY(bar.Low);
                    dc.DrawLine(pen, new Point(x, highY), new Point(x, lowY));
                    dc.DrawRectangle(brush, null, new Rect(x - 3, Math.Min(openY, closeY), 6, Math.Max(2, Math.Abs(openY - closeY))));

                    double volumeY = VolumeY(bar.Volume);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)), null, new Rect(x - 3, volumeY, 6, bottom - volumeY));

                    if (bar.DateTime == baseTime)
                        DrawMarker(dc, x, top, priceBottom, "BASE", Colors.Gold);
                    if (i == entryMarkerIndex)
                        DrawBuyArrow(dc, x, Math.Min(priceBottom - 18, PriceY(bar.Low) + 10), "BUY", Color.FromRgb(80, 220, 120));
                    if (i == exitMarkerIndex)
                        DrawMarker(dc, x, top, priceBottom, "SELL", Color.FromRgb(255, 120, 80));
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void DrawMarker(DrawingContext dc, double x, int top, int bottom, string label, Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 2);
            dc.DrawLine(pen, new Point(x, top), new Point(x, bottom));
            DrawText(dc, label, x + 4, top + 8, 11, color);
        }

        private static void DrawBuyArrow(DrawingContext dc, double x, double y, string label, Color color)
        {
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, 1.4);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x, y), true, true);
                ctx.LineTo(new Point(x - 7, y + 12), true, false);
                ctx.LineTo(new Point(x + 7, y + 12), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(brush, pen, geometry);
            DrawText(dc, label, x + 7, y + 4, 11, color);
        }

        private static void DrawMa(
            DrawingContext dc,
            IReadOnlyList<BacktestMinuteBar> bars,
            IReadOnlyList<double?> ma,
            int left,
            double xStep,
            Func<long, double> priceY,
            Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 1.5);
            Point? previous = null;
            for (int i = 0; i < bars.Count; i++)
            {
                if (!ma[i].HasValue)
                    continue;

                var current = new Point(left + i * xStep, priceY((long)ma[i]!.Value));
                if (previous.HasValue)
                    dc.DrawLine(pen, previous.Value, current);
                previous = current;
            }
        }

        private static List<double?> CalculateMa(IReadOnlyList<BacktestMinuteBar> bars, int period)
        {
            var result = new List<double?>(bars.Count);
            for (int i = 0; i < bars.Count; i++)
            {
                if (i < period - 1)
                {
                    result.Add(null);
                    continue;
                }

                result.Add((double)bars.Skip(i - period + 1).Take(period).Average(bar => (decimal)bar.Close));
            }

            return result;
        }

        private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                size,
                new SolidColorBrush(color),
                1.0);
            dc.DrawText(formatted, new Point(x, y));
        }

        private static int FindTimeIndex(IReadOnlyList<BacktestMinuteBar> bars, string dateTime)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                if (string.CompareOrdinal(bars[i].DateTime, dateTime) >= 0)
                    return i;
            }

            return -1;
        }

        private static int FindLastTimeIndexAtOrBefore(IReadOnlyList<BacktestMinuteBar> bars, string dateTime)
        {
            for (int i = bars.Count - 1; i >= 0; i--)
            {
                if (string.CompareOrdinal(bars[i].DateTime, dateTime) <= 0)
                    return i;
            }

            return -1;
        }

        private static int ResolveHoldingMinutes(string entryTime, string exitTime, int fallbackBars)
        {
            if (DateTime.TryParseExact(entryTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entry) &&
                DateTime.TryParseExact(exitTime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exit))
            {
                return Math.Max(1, (int)Math.Round((exit - entry).TotalMinutes));
            }

            return Math.Max(1, fallbackBars);
        }

        private static string ResolveDate(string dateTime)
        {
            string digits = new([.. (dateTime ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 8 ? digits[..8] : string.Empty;
        }

        private static string ResolveTime(string dateTime)
        {
            string digits = new([.. (dateTime ?? string.Empty).Where(char.IsDigit)]);
            return digits.Length >= 14 ? digits.Substring(8, 6) : string.Empty;
        }

        private static bool IsTradingWindow(string dateTime)
        {
            string time = ResolveTime(dateTime);
            return string.CompareOrdinal(time, "080000") >= 0 && string.CompareOrdinal(time, "200000") <= 0;
        }

        private static bool IsNear(long price, decimal target, decimal percent)
        {
            if (price <= 0 || target <= 0)
                return false;

            decimal diff = Math.Abs(price - target) / target * 100m;
            return diff <= percent;
        }

        private string BuildBaseReason(BaseState state)
        {
            if (state.Kind == "AVGIF240_LOWER_RECOVERY")
            {
                return $"completed daily MA aligned 5={state.DailyMa5:N0} > 20={state.DailyMa20:N0} > 60={state.DailyMa60:N0}; daily value {FormatEok(state.DailyTradingValue)}; AVGIF240 lower band recovery CROSSUP; lower={state.TriggerLine:N0}; 5m base={state.Time}; close {state.Close:N0}; base low {state.Low:N0}; prev close stop {state.StopPrice:N0}; MA200 {state.Ma200:N0}";
            }

            return $"completed daily MA aligned 5={state.DailyMa5:N0} > 20={state.DailyMa20:N0} > 60={state.DailyMa60:N0}; daily value {FormatEok(state.DailyTradingValue)}; A=prevHigh({state.PreviousDailyHigh:N0})-prevLow({state.PreviousDailyLow:N0}); B=dayOpen({state.DayOpen:N0})+A*{_rangeFactor.ToString("0.##", CultureInfo.InvariantCulture)}={state.TriggerLine:N0}; CROSSUP(C,B) and RSI2={state.Rsi2:0.##}>50; 5m base={state.Time}; close {state.Close:N0}; base low {state.Low:N0}; prev close stop {state.StopPrice:N0}; MA200 {state.Ma200:N0}";
        }

        private static string BuildEntryReason(BaseState state) =>
            $"buy at 5m signal candle close; formula CROSSUP(C,B) AND RSI2>50; daily value {FormatEok(state.DailyTradingValue)}; B={state.TriggerLine:N0}; RSI2={state.Rsi2:0.##}; base {state.Time}; base low {state.Low:N0}; prev close stop {state.StopPrice:N0}; center {state.Center:N0}; MA200 {state.Ma200:N0}";

        private static string FormatEok(long value) =>
            value > 0 ? $"{value / 100_000_000m:0.##}eok" : "-";

        private static string BuildEntryReason(BaseState state, decimal oneMinuteMa60) =>
            $"legacy 1m MA60 recovery high break; recovery {state.RecoveryTime} high {state.RecoveryHigh:N0}; base {state.Time}; 1m MA60 {oneMinuteMa60:N0}; center {state.Center:N0}; MA200 {state.Ma200:N0}";

        private static int ResolveMaxConsecutiveLosses(IEnumerable<BacktestTradeRow> trades)
        {
            int max = 0;
            int current = 0;
            foreach (BacktestTradeRow trade in trades)
            {
                if (trade.ProfitRate <= 0)
                {
                    current++;
                    max = Math.Max(max, current);
                }
                else
                {
                    current = 0;
                }
            }

            return max;
        }

        private readonly record struct RangeFactorDiagnosticRow(
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
        private readonly record struct StockMarketKey(string Code, string Market);
        private readonly record struct ChartRequest(StockMarketKey Stock, string BaseTime, string EntryTime, string ExitTime, long EntryPrice, long ExitPrice);
        private readonly record struct BaseState(
            string Time,
            long Open,
            long High,
            long Low,
            long Close,
            long Volume,
            long TradingValue,
            decimal PriceMa5,
            decimal PriceMa20,
            decimal PriceMa60,
            decimal Ma200,
            decimal VolumeMa5,
            decimal VolumeMa20,
            decimal VolumeMa60,
            decimal TradingValueMa5,
            decimal TradingValueMa20,
            decimal TradingValueMa60,
            decimal DailyMa5,
            decimal DailyMa20,
            decimal DailyMa60,
            long PreviousDailyHigh,
            long PreviousDailyLow,
            long DailyTradingValue,
            long DayOpen,
            decimal TriggerLine,
            decimal Rsi2,
            long StopPrice,
            decimal WeeklyMacd,
            decimal WeeklySignal,
            string RecoveryTime,
            long RecoveryHigh,
            decimal Center,
            string Kind);
        private readonly record struct DailyTrendState(
            long PreviousHigh,
            long PreviousLow,
            long PreviousClose,
            long DailyTradingValue,
            decimal Ma5,
            decimal Ma20,
            decimal Ma60)
        {
            public bool IsAligned => Ma5 > Ma20 && Ma20 > Ma60;
        }
        private readonly record struct MaState(
            string Time,
            long Close,
            decimal Ma,
            long PreviousClose,
            decimal PreviousMa);
        private readonly record struct WeeklyClose(string WeekEndDate, long Close);
        private readonly record struct WeeklyMacdSeed(string WeekEndDate, decimal Ema12, decimal Ema26, decimal Signal);
        private readonly record struct WeeklyMacdState(string WeekEndDate, decimal Macd, decimal Signal)
        {
            public bool IsBullish => Macd >= Signal;
        }
        private readonly record struct RecoveryState(string Time, long High, decimal Ma60);
        private readonly record struct IntradayRankSnapshot(StockMarketKey Stock, long CumulativeTradingValue, decimal ChangeRate);
        private readonly record struct IntradayRankState(
            string Time,
            long CumulativeTradingValue,
            decimal ChangeRate,
            int TradingValueRank,
            int ChangeRateRank)
        {
            public bool IsTop10Both =>
                TradingValueRank is >= 1 and <= 10 &&
                ChangeRateRank is >= 1 and <= 10;
        }
        private readonly record struct ExitResult(
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            int HoldingMinutes,
            string ExitReason);
        private readonly record struct LinearRegressionExitState(
            bool IsReady,
            bool FastCrossDown,
            bool SlowCrossDown,
            bool ShouldExit,
            double FastLine,
            double SlowLine);
        private readonly record struct Ma240DeviationUpperLine(
            decimal Ma240,
            decimal UpperLine,
            decimal PositiveDeviationAverage,
            decimal PositiveDeviationStdDev);
        private readonly record struct Ma240DeviationBandLine(
            decimal Ma240,
            decimal LowerLine,
            decimal UpperLine,
            decimal NegativeDeviationAverage,
            decimal NegativeDeviationStdDev,
            decimal PositiveDeviationAverage,
            decimal PositiveDeviationStdDev);
        private readonly record struct Ma240DeviationUpperExitState(
            bool IsReady,
            bool ShouldExit,
            decimal Ma240,
            decimal UpperLine,
            decimal PositiveDeviationAverage,
            decimal PositiveDeviationStdDev);
        private readonly record struct Ma240DeviationBandState(
            bool IsReady,
            bool LowerRecoveryCrossUp,
            bool UpperExtensionCrossUp,
            decimal Ma240,
            decimal LowerLine,
            decimal UpperLine,
            decimal NegativeDeviationAverage,
            decimal NegativeDeviationStdDev,
            decimal PositiveDeviationAverage,
            decimal PositiveDeviationStdDev);
        private readonly record struct EntryExitResult(
            string EntryTime,
            long EntryPrice,
            string ExitTime,
            long ExitPrice,
            long MaxHigh,
            long MinLow,
            long StopPrice,
            int HoldingMinutes,
            string EntryReason,
            string ExitReason);
    }
}
