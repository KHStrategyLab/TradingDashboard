using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public sealed class IntradayFifteenMinuteScalpStrategySlot
        : TradingStrategySlotBase
    {
        private const long OneEok = 100_000_000;
        private const long MinTodayTradeValue = 300 * OneEok;
        private const long MinBaseCandleTradeValue = 20 * OneEok;

        public IntradayFifteenMinuteScalpStrategySlot()
            : base(new StrategySlotDescriptor(
                StrategySlotId.IntradayFifteenMinuteScalp,
                "Intraday 15m Base + 1m Trigger",
                StrategyMarketScope.Sor,
                "DAY",
                "Today condition stock, 15m base candle, 1m MA/volume trigger",
                "Intraday scalp lane for stocks that entered the condition search today. It does not require the D+ daily gate. The final 1m trigger mixes MA5/MA60 recovery and volume MA expansion before order handoff.",
                "Docs/Strategies/intraday-15m-base-1m-trigger.md"))
        {
        }

        public override StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            bool hasStock = context.Stock != null;
            bool isTodayLane = context.Stock?.IsIntradayPreCandidate == true ||
                context.Stock?.GateBaseCandleOffset == 0;
            bool timeOk = IsTradingTime(DateTime.Now.TimeOfDay);
            double changeRate = ResolveChangeRate(context);
            long todayTradeValue = ResolveTodayTradeValue(context);
            bool changeOk = changeRate >= 7.0 && changeRate <= 24.0;
            bool tradeValueOk = todayTradeValue >= MinTodayTradeValue;
            bool notOwned = !context.IsOwned;

            IReadOnlyList<StrategyMinuteBar> fifteenBars = context.MinuteBars.TryGetValue(15, out IReadOnlyList<StrategyMinuteBar>? f15)
                ? f15
                : [];
            IReadOnlyList<StrategyMinuteBar> fiveBars = context.MinuteBars.TryGetValue(5, out IReadOnlyList<StrategyMinuteBar>? f5)
                ? f5
                : [];
            IReadOnlyList<StrategyMinuteBar> threeBars = context.MinuteBars.TryGetValue(3, out IReadOnlyList<StrategyMinuteBar>? f3)
                ? f3
                : [];
            IReadOnlyList<StrategyMinuteBar> oneBars = context.MinuteBars.TryGetValue(1, out IReadOnlyList<StrategyMinuteBar>? f1)
                ? f1
                : [];

            bool minuteReady = fifteenBars.Count >= 20 &&
                fiveBars.Count >= 60 &&
                threeBars.Count >= 20 &&
                oneBars.Count >= 60;

            StrategyMinuteBar? baseCandle = FindLatestFifteenMinuteBaseCandle(fifteenBars);
            bool hasBaseCandle = baseCandle != null;
            long baseCenter = hasBaseCandle ? (baseCandle!.High + baseCandle.Low) / 2 : 0;
            long baseHigh = baseCandle?.High ?? 0;
            long signalPrice = ResolveSignalPrice(context, oneBars, threeBars, fiveBars);
            bool baseSupport = hasBaseCandle &&
                signalPrice > baseCenter &&
                signalPrice < (long)Math.Round(baseHigh * 1.025, MidpointRounding.AwayFromZero);

            StrategyMinuteBar? fiveCurrent = fiveBars.LastOrDefault();
            StrategyMinuteBar? threeCurrent = threeBars.LastOrDefault();
            StrategyMinuteBar? threePrevious = threeBars.Count >= 2 ? threeBars[^2] : null;
            StrategyMinuteBar? oneCurrent = oneBars.LastOrDefault();
            StrategyMinuteBar? onePrevious = oneBars.Count >= 2 ? oneBars[^2] : null;

            bool fiveTrend = fiveCurrent != null &&
                fiveCurrent.Ma5 > 0 &&
                fiveCurrent.Ma20 > 0 &&
                fiveCurrent.Ma5 >= fiveCurrent.Ma20;
            bool fiveBull = fiveCurrent != null &&
                fiveCurrent.Close > fiveCurrent.Open &&
                fiveCurrent.Close > baseCenter;
            bool threeTurn = threeCurrent != null &&
                threePrevious != null &&
                threeCurrent.Close > threeCurrent.Open &&
                threeCurrent.Close > threePrevious.Close &&
                threeCurrent.Ma5 > 0 &&
                threeCurrent.Close > threeCurrent.Ma5;

            double volumeMa5 = AverageVolume(oneBars, 5);
            double volumeMa60 = AverageVolume(oneBars, 60);
            double previousVolumeMa5 = AverageVolume(oneBars.Take(Math.Max(0, oneBars.Count - 1)), 5);
            bool onePriceMaMix = oneCurrent != null &&
                oneCurrent.Ma5 > 0 &&
                oneCurrent.Ma60 > 0 &&
                oneCurrent.Close >= oneCurrent.Ma60 &&
                oneCurrent.Ma5 >= oneCurrent.Ma60;
            bool oneBreakout = oneCurrent != null &&
                onePrevious != null &&
                oneCurrent.High > onePrevious.High &&
                oneCurrent.Close > oneCurrent.Open;
            bool oneVolumeExpansion = oneCurrent != null &&
                previousVolumeMa5 > 0 &&
                oneCurrent.Volume >= previousVolumeMa5 * 1.5 &&
                volumeMa5 > 0 &&
                volumeMa60 > 0 &&
                volumeMa5 >= volumeMa60;

            StrategyRealtimeFlowSnapshot realtime = context.RealtimeFlow;
            DateTime now = DateTime.Now;
            bool realtimeTickFresh = realtime.HasFreshTick(now, 10);
            bool orderBookFresh = realtime.HasFreshOrderBook(now, 15);
            bool buyFlowOk = realtimeTickFresh &&
                realtime.LastTradeIsBuy &&
                realtime.BuyTradeVolume60s > realtime.SellTradeVolume60s &&
                realtime.BuyTradeVolumeRatio60s >= 55.0 &&
                realtime.BuyTradeValue60s >= 50_000_000;
            bool orderBookSupport = orderBookFresh &&
                realtime.TotalBidQuantity > 0 &&
                realtime.BidQuantityRatio >= 45.0 &&
                (realtime.BestBidPrice <= 0 || signalPrice >= realtime.BestBidPrice);
            bool realtimeFlowOk = buyFlowOk && orderBookSupport;

            bool preSignal = hasStock &&
                notOwned &&
                isTodayLane &&
                timeOk &&
                changeOk &&
                tradeValueOk &&
                minuteReady &&
                hasBaseCandle &&
                baseSupport &&
                fiveTrend &&
                fiveBull &&
                threeTurn &&
                onePriceMaMix &&
                oneBreakout &&
                oneVolumeExpansion;
            bool hasSignal = preSignal && realtimeFlowOk;

            List<string> noBuyReasons = BuildNoBuyReasons(
                hasStock,
                notOwned,
                isTodayLane,
                timeOk,
                changeOk,
                tradeValueOk,
                minuteReady,
                hasBaseCandle,
                baseSupport,
                fiveTrend && fiveBull,
                threeTurn,
                onePriceMaMix && oneBreakout && oneVolumeExpansion,
                realtimeTickFresh,
                buyFlowOk,
                orderBookFresh,
                orderBookSupport);

            StrategyOrderIntent orderIntent = hasSignal
                ? StrategyOrderIntent.BuyNow(
                    "intraday 15m base + 1m MA/volume/buy-flow trigger",
                    signalPrice,
                    stopPrice: baseCenter,
                    targetPrice: signalPrice > 0 ? (long)Math.Round(signalPrice * 1.018, MidpointRounding.AwayFromZero) : 0,
                    rewardRiskRatio: CalculateRewardRisk(signalPrice, baseCenter))
                : preSignal
                    ? StrategyOrderIntent.PrepareBuy("waiting for 0B buy-flow/order-book confirmation", noBuyReasons)
                    : StrategyOrderIntent.Watch(noBuyReasons.Count == 0 ? "tracking intraday scalp setup" : string.Join(" / ", noBuyReasons.Take(3)));

            StrategyProgressSnapshot progress = StrategyProgressCalculator.Build(
                Id,
                context.IsOwned ? "OWNED" : hasSignal ? "SIGNAL" : preSignal ? "ARMED" : "WAIT",
                context.IsOwned
                    ? "intraday exit tracking"
                    : hasSignal
                        ? "1m trigger confirmed"
                        : preSignal
                            ? "minute trigger ready; waiting realtime flow"
                            : "today condition / 15m base / 1m trigger tracking",
                [
                    StrategyProgressCalculator.Step("condition", "today condition lane", hasStock && isTodayLane),
                    StrategyProgressCalculator.Step("time", "09:05-14:50", timeOk),
                    StrategyProgressCalculator.Step("change", "7-24% change", changeOk),
                    StrategyProgressCalculator.Step("trade-value", "300eok today value", tradeValueOk),
                    StrategyProgressCalculator.Step("minute-data", "1/3/5/15m ready", minuteReady),
                    StrategyProgressCalculator.Step("base-15m", "15m base candle", hasBaseCandle),
                    StrategyProgressCalculator.Step("base-support", "base center support", baseSupport),
                    StrategyProgressCalculator.Step("5m-confirm", "5m MA/bull confirm", fiveTrend && fiveBull),
                    StrategyProgressCalculator.Step("3m-turn", "3m turn", threeTurn),
                    StrategyProgressCalculator.Step("1m-ma", "1m MA5 over MA60", onePriceMaMix),
                    StrategyProgressCalculator.Step("1m-volume", "1m volume MA expansion", oneVolumeExpansion),
                    StrategyProgressCalculator.Step("1m-breakout", "1m high breakout", oneBreakout),
                    StrategyProgressCalculator.Step("0b-fresh", "0B fresh", realtimeTickFresh),
                    StrategyProgressCalculator.Step("0b-flow", "0B buy-flow", buyFlowOk),
                    StrategyProgressCalculator.Step("0d-fresh", "0D fresh", orderBookFresh),
                    StrategyProgressCalculator.Step("0d-support", "0D bid support", orderBookSupport),
                    StrategyProgressCalculator.Step("buy", "buy filled", context.IsOwned)
                ],
                [
                    StrategyProgressCalculator.Step("stop", "base center / -1.5 stop", false),
                    StrategyProgressCalculator.Step("target1", "+1.5 scale", false),
                    StrategyProgressCalculator.Step("target2", "+2.7 scale", false),
                    StrategyProgressCalculator.Step("trail", "+1 trail", false),
                    StrategyProgressCalculator.Step("timecut", "420s time cut", false),
                    StrategyProgressCalculator.Step("exit", "exit done", false)
                ],
                isOwned: context.IsOwned,
                levelText: hasBaseCandle ? $"{baseCandle!.BucketTime:HH:mm} base" : "-",
                strengthPercent: hasSignal ? 70 : preSignal ? 66 : hasBaseCandle ? 44 : isTodayLane ? 18 : 0,
                strengthLabel: "intraday");

            return new StrategyEvaluationResult(
                Id,
                Name,
                hasSignal,
                context.IsOwned ? "TRACK" : hasSignal ? "SIGNAL" : preSignal ? "ARMED" : "WAIT",
                FormatSummary(changeRate, todayTradeValue, baseCandle, baseCenter, signalPrice, volumeMa5, volumeMa60, realtime, noBuyReasons),
                progress,
                orderIntent);
        }

        private static bool IsTradingTime(TimeSpan now) =>
            now >= TimeSpan.Parse("09:05:00", CultureInfo.InvariantCulture) &&
            now <= TimeSpan.Parse("14:50:00", CultureInfo.InvariantCulture);

        private static StrategyMinuteBar? FindLatestFifteenMinuteBaseCandle(IReadOnlyList<StrategyMinuteBar> bars)
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
                    (bar.Close - bar.Open) / (double)bar.Open * 100.0 >= 1.5 &&
                    bar.TradingValue >= MinBaseCandleTradeValue)
                .OrderByDescending(bar => bar.BucketTime)
                .FirstOrDefault();
        }

        private static long ResolveSignalPrice(
            StrategyEvaluationContext context,
            IReadOnlyList<StrategyMinuteBar> oneBars,
            IReadOnlyList<StrategyMinuteBar> threeBars,
            IReadOnlyList<StrategyMinuteBar> fiveBars)
        {
            if (oneBars.LastOrDefault()?.Close > 0)
                return oneBars.Last().Close;
            if (threeBars.LastOrDefault()?.Close > 0)
                return threeBars.Last().Close;
            if (fiveBars.LastOrDefault()?.Close > 0)
                return fiveBars.Last().Close;
            if (context.Stock?.CurrentPrice > 0)
                return context.Stock.CurrentPrice;
            return context.Stock?.LastPrice ?? 0;
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

        private static double CalculateRewardRisk(long entry, long stop)
        {
            if (entry <= 0 || stop <= 0 || entry <= stop)
                return 0;

            double risk = entry - stop;
            double target = entry * 1.018 - entry;
            return risk > 0 ? target / risk : 0;
        }

        private static List<string> BuildNoBuyReasons(
            bool hasStock,
            bool notOwned,
            bool isTodayLane,
            bool timeOk,
            bool changeOk,
            bool tradeValueOk,
            bool minuteReady,
            bool hasBaseCandle,
            bool baseSupport,
            bool fiveConfirm,
            bool threeTurn,
            bool oneTrigger,
            bool realtimeTickFresh,
            bool buyFlowOk,
            bool orderBookFresh,
            bool orderBookSupport)
        {
            List<string> reasons = [];
            if (!hasStock) reasons.Add("no stock");
            if (!notOwned) reasons.Add("owned or duplicate blocked");
            if (!isTodayLane) reasons.Add("not today lane");
            if (!timeOk) reasons.Add("outside time");
            if (!changeOk) reasons.Add("change filter");
            if (!tradeValueOk) reasons.Add("today value filter");
            if (!minuteReady) reasons.Add("minute data wait");
            if (!hasBaseCandle) reasons.Add("15m base wait");
            if (!baseSupport) reasons.Add("base support wait");
            if (!fiveConfirm) reasons.Add("5m confirm wait");
            if (!threeTurn) reasons.Add("3m turn wait");
            if (!oneTrigger) reasons.Add("1m trigger wait");
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
            double volumeMa5,
            double volumeMa60,
            StrategyRealtimeFlowSnapshot realtime,
            IReadOnlyList<string> noBuyReasons)
        {
            string baseText = baseCandle == null
                ? "15m base -"
                : $"15m base {baseCandle.BucketTime:HH:mm} center {baseCenter:N0}";
            string waitText = noBuyReasons.Count == 0
                ? "ready"
                : $"wait {string.Join(", ", noBuyReasons.Take(2))}";
            return $"intraday {changeRate:0.##}% / {todayTradeValue / (double)OneEok:0.#}eok / {baseText} / price {signalPrice:N0} / volMA5 {volumeMa5:0} vs 60 {volumeMa60:0} / 0B buy {realtime.BuyTradeVolumeRatio60s:0}% / 0D bid {realtime.BidQuantityRatio:0}% / {waitText}";
        }
    }
}
