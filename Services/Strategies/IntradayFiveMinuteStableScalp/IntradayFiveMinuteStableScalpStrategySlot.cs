using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public sealed class IntradayFiveMinuteStableScalpStrategySlot
        : TradingStrategySlotBase
    {
        private const long OneEok = 100_000_000;
        private const long MinTodayTradeValue = 100 * OneEok;
        private const long MinBaseCandleTradeValue = 5 * OneEok;
        private const int BaseMinute = 5;
        private const int TriggerMinute = 1;

        public IntradayFiveMinuteStableScalpStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.IntradayFiveMinuteStableScalp,
                $"DAY {BaseMinute}m Base + {TriggerMinute}m Stable",
                StrategyMarketScope.Sor,
                "DAY",
                $"{BaseMinute}m base candle support + {TriggerMinute}m MA/volume stable trigger",
                $"Fast practice lane. Uses an N-minute base candle as the small structure (current N={BaseMinute}), checks 10m/15m context, then waits for {TriggerMinute}m MA5/MA60 recovery, volume MA expansion, and small-hill breakout before order handoff.",
                "Docs/Strategies/intraday-5m-base-1m-stable.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool isTodayLane = context.Stock?.IsIntradayPreCandidate == true ||
                context.Stock?.GateBaseCandleOffset == 0 ||
                context.Stock?.GateBaseCandleFound == true;
            bool timeOk = IsTradingTime(DateTime.Now.TimeOfDay);
            bool notOwned = !context.IsOwned;
            double changeRate = ResolveChangeRate(context);
            long todayTradeValue = ResolveTodayTradeValue(context);
            bool changeOk = changeRate >= 2.0 && changeRate <= 24.0;
            bool tradeValueOk = todayTradeValue >= MinTodayTradeValue;

            IReadOnlyList<StrategyMinuteBar> fifteenBars = GetBars(context, 15);
            IReadOnlyList<StrategyMinuteBar> tenBars = GetBars(context, 10);
            IReadOnlyList<StrategyMinuteBar> fiveBars = GetBars(context, BaseMinute);
            IReadOnlyList<StrategyMinuteBar> oneBars = GetBars(context, TriggerMinute);

            bool minuteReady = fifteenBars.Count >= 60 &&
                tenBars.Count >= 60 &&
                fiveBars.Count >= 60 &&
                oneBars.Count >= 60;

            StrategyMinuteBar? baseCandle = FindLatestBaseMinuteCandle(fiveBars);
            bool hasBaseCandle = baseCandle != null;
            long baseCenter = hasBaseCandle ? (baseCandle!.High + baseCandle.Low) / 2 : 0;
            long baseHigh = baseCandle?.High ?? 0;
            long signalPrice = ResolveSignalPrice(context, oneBars, fiveBars);

            StrategyMinuteBar? fifteenCurrent = fifteenBars.LastOrDefault();
            StrategyMinuteBar? tenCurrent = tenBars.LastOrDefault();
            StrategyMinuteBar? fiveCurrent = fiveBars.LastOrDefault();
            StrategyMinuteBar? oneCurrent = oneBars.LastOrDefault();
            StrategyMinuteBar? onePrevious = oneBars.Count >= 2 ? oneBars[^2] : null;

            bool higherFrameNotHostile = IsNotHostileAgainstMa60(signalPrice, fifteenCurrent) &&
                IsNotHostileAgainstMa60(signalPrice, tenCurrent);
            bool fiveBaseSupport = hasBaseCandle &&
                signalPrice > 0 &&
                baseCenter > 0 &&
                signalPrice >= (long)Math.Round(baseCenter * 0.995, MidpointRounding.AwayFromZero) &&
                signalPrice <= (long)Math.Round(baseHigh * 1.025, MidpointRounding.AwayFromZero);
            bool fiveTrendSupport = fiveCurrent != null &&
                fiveCurrent.Ma5 > 0 &&
                fiveCurrent.Ma20 > 0 &&
                fiveCurrent.Ma60 > 0 &&
                fiveCurrent.Close >= fiveCurrent.Ma60 * 0.995 &&
                fiveCurrent.Ma5 >= fiveCurrent.Ma20 * 0.995;

            double oneVolumeMa5 = AverageVolume(oneBars, 5);
            double oneVolumeMa20 = AverageVolume(oneBars, 20);
            double oneVolumeMa60 = AverageVolume(oneBars, 60);
            double previousVolumeMa5 = AverageVolume(oneBars.Take(Math.Max(0, oneBars.Count - 1)), 5);
            long oneSmallHillHigh = PreviousHigh(oneBars, 5);
            long recentOnePullbackLow = RecentLow(oneBars, 6);

            bool oneMaRecovery = oneCurrent != null &&
                oneCurrent.Ma5 > 0 &&
                oneCurrent.Ma60 > 0 &&
                oneCurrent.Close >= oneCurrent.Ma60 &&
                oneCurrent.Ma5 >= oneCurrent.Ma60 * 0.998;
            bool oneBullishTurn = oneCurrent != null &&
                onePrevious != null &&
                oneCurrent.Close > oneCurrent.Open &&
                oneCurrent.Close >= onePrevious.Close;
            bool oneVolumeExpansion = oneCurrent != null &&
                previousVolumeMa5 > 0 &&
                oneCurrent.Volume >= previousVolumeMa5 * 1.2 &&
                oneVolumeMa5 > 0 &&
                oneVolumeMa20 > 0 &&
                oneVolumeMa5 >= oneVolumeMa20 &&
                (oneVolumeMa60 <= 0 || oneVolumeMa5 >= oneVolumeMa60 * 0.85);
            bool oneSmallBreakout = signalPrice > 0 &&
                oneSmallHillHigh > 0 &&
                signalPrice >= oneSmallHillHigh;
            bool prevHighPlusOneTouched = IsPreviousDayHighPlusOneTouched(context, signalPrice);
            bool bollingerLowerRecovery = IsBollingerLowerRecovery(oneBars);
            bool ma200PullbackRecovery = IsMa200PullbackOrRecovery(oneBars);

            StrategyRealtimeFlowSnapshot realtime = context.RealtimeFlow;
            DateTime now = DateTime.Now;
            bool realtimeTickFresh = realtime.HasFreshTick(now, 10);
            bool orderBookFresh = realtime.HasFreshOrderBook(now, 15);
            bool buyFlowOk = realtimeTickFresh &&
                realtime.LastTradeIsBuy &&
                realtime.BuyTradeVolume60s > realtime.SellTradeVolume60s &&
                realtime.BuyTradeVolumeRatio60s >= 52.0 &&
                realtime.BuyTradeValue60s >= 20_000_000;
            bool orderBookSupport = orderBookFresh &&
                realtime.TotalBidQuantity > 0 &&
                realtime.BidQuantityRatio >= 43.0 &&
                (realtime.BestBidPrice <= 0 || signalPrice >= realtime.BestBidPrice);
            bool realtimeFlowOk = buyFlowOk && orderBookSupport;

            long stopAnchor = ResolveStopAnchor(signalPrice, baseCenter, recentOnePullbackLow, oneCurrent);
            long targetPrice = signalPrice > 0 && stopAnchor > 0
                ? signalPrice + (long)Math.Round((signalPrice - stopAnchor) * 1.9, MidpointRounding.AwayFromZero)
                : 0;
            StrategyExitFirstPlan exitPlan = StrategyExitFirstPlanner.Build(
                signalPrice,
                stopAnchor,
                targetPrice,
                minRewardRiskRatio: 1.5,
                minStopRiskPercent: 0.2,
                maxStopRiskPercent: 1.8,
                fallbackTargetRewardMultiple: 1.9);
            StrategyExitBreakEvaluation exitBreak = StrategyExitBreakEvaluator.Evaluate(oneBars, exitPlan.StopPrice);

            bool preSignal = hasStock &&
                notOwned &&
                isTodayLane &&
                timeOk &&
                changeOk &&
                tradeValueOk &&
                minuteReady &&
                higherFrameNotHostile &&
                hasBaseCandle &&
                fiveBaseSupport &&
                fiveTrendSupport &&
                oneMaRecovery &&
                oneBullishTurn &&
                oneVolumeExpansion &&
                oneSmallBreakout;
            bool hasSignal = preSignal && realtimeFlowOk && exitPlan.IsTradable;

            List<string> noBuyReasons = BuildNoBuyReasons(
                hasStock,
                notOwned,
                isTodayLane,
                timeOk,
                changeOk,
                tradeValueOk,
                minuteReady,
                higherFrameNotHostile,
                hasBaseCandle,
                fiveBaseSupport,
                fiveTrendSupport,
                oneMaRecovery,
                oneBullishTurn,
                oneVolumeExpansion,
                oneSmallBreakout,
                realtimeTickFresh,
                buyFlowOk,
                orderBookFresh,
                orderBookSupport);
            noBuyReasons.AddRange(exitPlan.NoBuyReasons);

            string prepareReason = preSignal && realtimeFlowOk && !exitPlan.IsTradable
                ? "exit-first blocked before order handoff"
                : "waiting for 0B buy-flow/order-book confirmation";

            StrategyOrderIntent orderIntent = hasSignal
                ? StrategyOrderIntent.BuyNow(
                    "5m base support + 1m stable trigger",
                    signalPrice,
                    stopPrice: exitPlan.StopPrice,
                    targetPrice: exitPlan.TargetPrice,
                    rewardRiskRatio: exitPlan.RewardRiskRatio)
                : preSignal
                    ? StrategyOrderIntent.PrepareBuy(prepareReason, noBuyReasons)
                    : StrategyOrderIntent.Watch(noBuyReasons.Count == 0 ? "tracking 5m base / 1m stable setup" : string.Join(" / ", noBuyReasons.Take(3)));

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned && exitBreak.ShouldExit ? "EXIT" : context.IsOwned ? "OWNED" : hasSignal ? "SIGNAL" : preSignal ? "ARMED" : "WAIT",
                context.IsOwned
                    ? $"stable scalp exit tracking / {exitBreak.Reason}"
                    : hasSignal
                        ? "1m stable trigger confirmed"
                        : preSignal
                            ? "5m base ready; waiting realtime flow"
                            : "5m base / 1m stable trigger tracking",
                [
                    StrategyProgressCalculator.Step("condition", "today/D+ lane", hasStock && isTodayLane),
                    StrategyProgressCalculator.Step("time", "09:05-14:50", timeOk),
                    StrategyProgressCalculator.Step("change", "2-24% change", changeOk),
                    StrategyProgressCalculator.Step("trade-value", "100eok today value", tradeValueOk),
                    StrategyProgressCalculator.Step("minute-data", "1/5/10/15m ready", minuteReady),
                    StrategyProgressCalculator.Step("context", "10/15m not hostile", higherFrameNotHostile),
                    StrategyProgressCalculator.Step("base-nm", $"{BaseMinute}m base candle", hasBaseCandle),
                    StrategyProgressCalculator.Step("base-support", $"{BaseMinute}m base waist support", fiveBaseSupport),
                    StrategyProgressCalculator.Step("base-trend", $"{BaseMinute}m MA support", fiveTrendSupport),
                    StrategyProgressCalculator.Step("trigger-ma", $"{TriggerMinute}m MA5 near/over MA60", oneMaRecovery),
                    StrategyProgressCalculator.Step("trigger-turn", $"{TriggerMinute}m bullish turn", oneBullishTurn),
                    StrategyProgressCalculator.Step("trigger-volume", $"{TriggerMinute}m volume MA expansion", oneVolumeExpansion),
                    StrategyProgressCalculator.Step("trigger-breakout", $"{TriggerMinute}m small-hill breakout", oneSmallBreakout),
                    StrategyProgressCalculator.Step("prev-high-1", "prev high +1% touched", prevHighPlusOneTouched),
                    StrategyProgressCalculator.Step("bb-lower", "BB lower recovery", bollingerLowerRecovery),
                    StrategyProgressCalculator.Step("ma200-recovery", "MA200 pullback/recovery", ma200PullbackRecovery),
                    StrategyProgressCalculator.Step("0b-fresh", "0B fresh", realtimeTickFresh),
                    StrategyProgressCalculator.Step("0b-flow", "0B buy-flow", buyFlowOk),
                    StrategyProgressCalculator.Step("0d-fresh", "0D fresh", orderBookFresh),
                    StrategyProgressCalculator.Step("0d-support", "0D bid support", orderBookSupport),
                    StrategyProgressCalculator.Step("exit-first", "exit-first RR", exitPlan.IsTradable),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", StrategyExitBreakEvaluator.ToProgressLabel(exitBreak), context.IsOwned && exitBreak.ShouldExit),
                    StrategyProgressCalculator.Step("target", "small segment target", false),
                    StrategyProgressCalculator.Step("trail", "trail if extends", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                levelText: hasBaseCandle ? $"{baseCandle!.BucketTime:HH:mm} {BaseMinute}m" : "-",
                strengthPercent: hasSignal ? 70 : preSignal ? 66 : hasBaseCandle ? 46 : isTodayLane ? 18 : 0,
                strengthLabel: "stable");

            return new StrategyEvaluationResult(
                Id,
                Name,
                hasSignal,
                context.IsOwned && exitBreak.ShouldExit ? "EXIT" : context.IsOwned ? "TRACK" : hasSignal ? "SIGNAL" : preSignal ? "ARMED" : "WAIT",
                context.IsOwned
                    ? $"{FormatSummary(changeRate, todayTradeValue, baseCandle, baseCenter, signalPrice, oneSmallHillHigh, oneVolumeMa5, oneVolumeMa20, prevHighPlusOneTouched, bollingerLowerRecovery, ma200PullbackRecovery, realtime, exitPlan, noBuyReasons)} / {exitBreak.Reason}"
                    : FormatSummary(changeRate, todayTradeValue, baseCandle, baseCenter, signalPrice, oneSmallHillHigh, oneVolumeMa5, oneVolumeMa20, prevHighPlusOneTouched, bollingerLowerRecovery, ma200PullbackRecovery, realtime, exitPlan, noBuyReasons),
                progress,
                orderIntent);
        }

        private static bool IsTradingTime(TimeSpan now) =>
            now >= TimeSpan.Parse("09:05:00", CultureInfo.InvariantCulture) &&
            now <= TimeSpan.Parse("14:50:00", CultureInfo.InvariantCulture);

        private static IReadOnlyList<StrategyMinuteBar> GetBars(StrategyEvaluationContext context, int minute) =>
            context.MinuteBars.TryGetValue(minute, out IReadOnlyList<StrategyMinuteBar>? bars)
                ? bars
                : [];

        // N-minute base candle detector. BaseMinute is 5 for now, but this stays isolated
        // so the slot can later expose N as a UI/config value without rewriting the strategy.
        private static StrategyMinuteBar? FindLatestBaseMinuteCandle(IReadOnlyList<StrategyMinuteBar> bars)
        {
            if (bars.Count == 0)
                return null;

            DateTime today = DateTime.Today;
            DateTime latestBucket = bars[^1].BucketTime;
            IEnumerable<StrategyMinuteBar> completedToday = bars
                .Where(bar => bar.BucketTime.Date == today && bar.BucketTime < latestBucket);

            return completedToday
                .Where(bar =>
                    bar.Open > 0 &&
                    bar.Close > bar.Open &&
                    (bar.Close - bar.Open) / (double)bar.Open * 100.0 >= 0.8 &&
                    bar.TradingValue >= MinBaseCandleTradeValue &&
                    bar.Close >= bar.High * 0.992 &&
                    (bar.Ma60 <= 0 || bar.Close >= bar.Ma60 * 0.995))
                .OrderByDescending(bar => bar.BucketTime)
                .FirstOrDefault();
        }

        private static bool IsNotHostileAgainstMa60(long signalPrice, StrategyMinuteBar? bar)
        {
            if (bar == null || signalPrice <= 0 || bar.Ma60 <= 0)
                return true;

            return signalPrice >= bar.Ma60 * 0.985;
        }

        private static long ResolveSignalPrice(
            StrategyEvaluationContext context,
            IReadOnlyList<StrategyMinuteBar> oneBars,
            IReadOnlyList<StrategyMinuteBar> fiveBars)
        {
            if (oneBars.LastOrDefault()?.Close > 0)
                return oneBars.Last().Close;
            if (fiveBars.LastOrDefault()?.Close > 0)
                return fiveBars.Last().Close;
            if (context.Stock?.CurrentPrice > 0)
                return context.Stock.CurrentPrice;
            return context.Stock?.LastPrice ?? 0;
        }

        private static long ResolveStopAnchor(
            long signalPrice,
            long baseCenter,
            long recentOnePullbackLow,
            StrategyMinuteBar? oneCurrent)
        {
            List<long> candidates = [];
            if (baseCenter > 0 && baseCenter < signalPrice)
                candidates.Add(baseCenter);
            if (recentOnePullbackLow > 0 && recentOnePullbackLow < signalPrice)
                candidates.Add(recentOnePullbackLow);
            if (oneCurrent?.Ma60 > 0 && oneCurrent.Ma60 < signalPrice)
                candidates.Add((long)Math.Round(oneCurrent.Ma60, MidpointRounding.AwayFromZero));

            return candidates.Count == 0 ? 0 : candidates.Max();
        }

        private static double ResolveChangeRate(StrategyEvaluationContext context)
        {
            string? text = context.Stock?.ChangeRateText;
            if (string.IsNullOrWhiteSpace(text) || text == "-")
                text = context.Metrics.ChangeRateText;

            text = (text ?? string.Empty)
                .Replace("%", string.Empty)
                .Replace("+", string.Empty)
                .Replace(",", string.Empty)
                .Trim();

            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
                ? value
                : 0;
        }

        private static long ResolveTodayTradeValue(StrategyEvaluationContext context)
        {
            if (context.Stock?.TodayTradeValue > 0)
                return context.Stock.TodayTradeValue;
            if (context.Metrics.TradingValue > 0)
                return context.Metrics.TradingValue;
            return 0;
        }

        private static double AverageVolume(IEnumerable<StrategyMinuteBar> source, int period)
        {
            List<StrategyMinuteBar> bars = [.. source.Where(bar => bar.Volume > 0).TakeLast(period)];
            return bars.Count >= period ? bars.Average(bar => bar.Volume) : 0;
        }

        private static long PreviousHigh(IReadOnlyList<StrategyMinuteBar> bars, int period)
        {
            if (bars.Count <= 1)
                return 0;

            List<StrategyMinuteBar> previous = [.. bars.Take(bars.Count - 1).TakeLast(period).Where(bar => bar.High > 0)];
            return previous.Count == 0 ? 0 : previous.Max(bar => bar.High);
        }

        private static long RecentLow(IReadOnlyList<StrategyMinuteBar> bars, int period)
        {
            List<StrategyMinuteBar> recent = [.. bars.TakeLast(period).Where(bar => bar.Low > 0)];
            return recent.Count == 0 ? 0 : recent.Min(bar => bar.Low);
        }

        private static bool IsPreviousDayHighPlusOneTouched(StrategyEvaluationContext context, long signalPrice)
        {
            long previousHigh = context.PreviousDayHigh;
            if (previousHigh <= 0)
                return false;

            long observedHigh = Math.Max(signalPrice, context.Stock?.MiniDailyHigh ?? 0);
            return observedHigh >= (long)Math.Round(previousHigh * 1.01, MidpointRounding.AwayFromZero);
        }

        private static bool IsBollingerLowerRecovery(IReadOnlyList<StrategyMinuteBar> bars)
        {
            if (bars.Count < 21)
                return false;

            StrategyMinuteBar previous = bars[^2];
            StrategyMinuteBar current = bars[^1];
            double? previousLower = ResolveBollingerLower(bars.Take(bars.Count - 1), 20, 2.0);
            double? currentLower = ResolveBollingerLower(bars, 20, 2.0);
            if (!previousLower.HasValue || !currentLower.HasValue)
                return false;

            bool intrabarRecovery = current.Low > 0 &&
                current.Low < currentLower.Value &&
                current.Close > currentLower.Value;
            bool closeCrossRecovery = previous.Close <= previousLower.Value &&
                current.Close > currentLower.Value;

            return intrabarRecovery || closeCrossRecovery;
        }

        private static bool IsMa200PullbackOrRecovery(IReadOnlyList<StrategyMinuteBar> bars)
        {
            if (bars.Count < 2)
                return false;

            StrategyMinuteBar previous = bars[^2];
            StrategyMinuteBar current = bars[^1];
            if (current.Ma200 <= 0)
                return false;

            bool ma200NotFalling = previous.Ma200 <= 0 || current.Ma200 >= previous.Ma200 * 0.999;
            bool nearMa200Pullback = current.Low > 0 &&
                current.Low <= current.Ma200 * 1.012 &&
                current.Close >= current.Ma200 * 0.995;
            bool ma200RecoveryCross = previous.Ma200 > 0 &&
                previous.Close <= previous.Ma200 &&
                current.Close > current.Ma200;

            return ma200NotFalling && (nearMa200Pullback || ma200RecoveryCross);
        }

        private static double? ResolveBollingerLower(IEnumerable<StrategyMinuteBar> source, int period, double deviationMultiplier)
        {
            List<double> closes = [.. source
                .Where(bar => bar.Close > 0)
                .TakeLast(period)
                .Select(bar => (double)bar.Close)];
            if (closes.Count < period)
                return null;

            double average = closes.Average();
            double variance = closes.Sum(value => Math.Pow(value - average, 2)) / closes.Count;
            double standardDeviation = Math.Sqrt(variance);
            return average - standardDeviation * deviationMultiplier;
        }

        private static List<string> BuildNoBuyReasons(
            bool hasStock,
            bool notOwned,
            bool isTodayLane,
            bool timeOk,
            bool changeOk,
            bool tradeValueOk,
            bool minuteReady,
            bool contextOk,
            bool hasBaseCandle,
            bool baseSupport,
            bool fiveTrendSupport,
            bool oneMaRecovery,
            bool oneBullishTurn,
            bool oneVolumeExpansion,
            bool oneSmallBreakout,
            bool realtimeTickFresh,
            bool buyFlowOk,
            bool orderBookFresh,
            bool orderBookSupport)
        {
            List<string> reasons = [];
            if (!hasStock) reasons.Add("no stock");
            if (!notOwned) reasons.Add("owned or duplicate blocked");
            if (!isTodayLane) reasons.Add("not today/D+ lane");
            if (!timeOk) reasons.Add("outside time");
            if (!changeOk) reasons.Add("change filter");
            if (!tradeValueOk) reasons.Add("today value filter");
            if (!minuteReady) reasons.Add("minute data wait");
            if (!contextOk) reasons.Add("10/15m context hostile");
            if (!hasBaseCandle) reasons.Add("5m base wait");
            if (!baseSupport) reasons.Add("5m base waist support wait");
            if (!fiveTrendSupport) reasons.Add("5m MA support wait");
            if (!oneMaRecovery) reasons.Add("1m MA recovery wait");
            if (!oneBullishTurn) reasons.Add("1m bullish turn wait");
            if (!oneVolumeExpansion) reasons.Add("1m volume expansion wait");
            if (!oneSmallBreakout) reasons.Add("1m small breakout wait");
            if (!realtimeTickFresh) reasons.Add("0B fresh tick wait");
            if (!buyFlowOk) reasons.Add("0B buy-flow wait");
            if (!orderBookFresh) reasons.Add("0D order-book wait");
            if (!orderBookSupport) reasons.Add("0D bid support wait");
            return reasons;
        }

        private static string FormatSummary(
            double changeRate,
            long todayTradeValue,
            StrategyMinuteBar? baseCandle,
            long baseCenter,
            long signalPrice,
            long oneSmallHillHigh,
            double volumeMa5,
            double volumeMa20,
            bool prevHighPlusOneTouched,
            bool bollingerLowerRecovery,
            bool ma200PullbackRecovery,
            StrategyRealtimeFlowSnapshot realtime,
            StrategyExitFirstPlan exitPlan,
            IReadOnlyList<string> noBuyReasons)
        {
            string baseText = baseCandle == null
                ? $"{BaseMinute}m base -"
                : $"{BaseMinute}m base {baseCandle.BucketTime:HH:mm} waist {baseCenter:N0}";
            string waitText = noBuyReasons.Count == 0
                ? "ready"
                : $"wait {string.Join(", ", noBuyReasons.Take(2))}";
            string helperText = $"prevH+1 {(prevHighPlusOneTouched ? "Y" : "N")} / BB {(bollingerLowerRecovery ? "Y" : "N")} / MA200 {(ma200PullbackRecovery ? "Y" : "N")}";
            return $"stable {changeRate:0.##}% / {todayTradeValue / (double)OneEok:0.#}eok / {baseText} / price {signalPrice:N0} / 1m hill {oneSmallHillHigh:N0} / volMA5 {volumeMa5:0} vs 20 {volumeMa20:0} / {helperText} / 0B buy {realtime.BuyTradeVolumeRatio60s:0}% / 0D bid {realtime.BidQuantityRatio:0}% / {StrategyExitFirstPlanner.FormatSummary(exitPlan)} / {waitText}";
        }
    }
}
