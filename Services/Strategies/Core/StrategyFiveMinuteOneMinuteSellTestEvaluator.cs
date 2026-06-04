using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public enum StrategyFiveMinuteOneMinuteSellTestAction
    {
        Hold,
        Watch,
        ScaleOut,
        ProtectProfit,
        StopAll
    }

    public sealed record StrategyFiveMinuteOneMinuteSellTestEvaluation(
        bool IsReady,
        StrategyFiveMinuteOneMinuteSellTestAction Action,
        bool ShouldSellCandidate,
        bool IsFullExitCandidate,
        int SellWeightPercent,
        string Severity,
        string Reason,
        string Market,
        DateTime EvaluatedAt,
        long CurrentPrice,
        long FiveMinuteSupportLow,
        bool OneMinuteWeak,
        bool FiveMinuteWeak,
        bool FiveMinuteSupportBroken,
        bool OneMinuteBearish,
        bool OneMinuteHighVolume,
        bool OneMinuteHighTradingValue,
        bool OneMinuteUpperTail,
        bool MarketSellPressure,
        double OneMinuteVolumeRatio,
        double OneMinuteTradingValueRatio,
        double UpperTailRatio,
        double MarketBuyRatio60s)
    {
        public static StrategyFiveMinuteOneMinuteSellTestEvaluation NotReady(string market, string reason) =>
            new(
                false,
                StrategyFiveMinuteOneMinuteSellTestAction.Hold,
                false,
                false,
                0,
                "NotReady",
                reason,
                NormalizeMarket(market),
                DateTime.Now,
                0,
                0,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                0,
                0,
                0,
                0);

        private static string NormalizeMarket(string market) =>
            string.Equals((market ?? string.Empty).Trim(), "NXT", StringComparison.OrdinalIgnoreCase)
                ? "NXT"
                : "KRX";
    }

    public static class StrategyFiveMinuteOneMinuteSellTestEvaluator
    {
        public static StrategyFiveMinuteOneMinuteSellTestEvaluation Evaluate(
            string market,
            IReadOnlyList<StrategyMinuteBar> oneMinuteBars,
            IReadOnlyList<StrategyMinuteBar> fiveMinuteBars,
            StrategyRealtimeFlowSnapshot realtimeFlow,
            int supportLookback = 20,
            double highVolumeMultiple = 1.5,
            double highTradingValueMultiple = 1.5,
            double upperTailThreshold = 0.45,
            double marketSellPressureBuyRatioThreshold = 45d)
        {
            string normalizedMarket = NormalizeMarket(market);
            if (oneMinuteBars.Count < 20)
                return StrategyFiveMinuteOneMinuteSellTestEvaluation.NotReady(normalizedMarket, "1m bars not ready");

            if (fiveMinuteBars.Count < 20)
                return StrategyFiveMinuteOneMinuteSellTestEvaluation.NotReady(normalizedMarket, "5m bars not ready");

            StrategyMinuteBar one = oneMinuteBars[^1];
            StrategyMinuteBar five = fiveMinuteBars[^1];
            if (one.Close <= 0 || five.Close <= 0)
                return StrategyFiveMinuteOneMinuteSellTestEvaluation.NotReady(normalizedMarket, "latest bar price missing");

            IReadOnlyList<StrategyMinuteBar> previousOneMinuteBars = [.. oneMinuteBars.Take(Math.Max(0, oneMinuteBars.Count - 1)).TakeLast(20)];
            IReadOnlyList<StrategyMinuteBar> previousFiveMinuteBars = [.. fiveMinuteBars.Take(Math.Max(0, fiveMinuteBars.Count - 1)).TakeLast(Math.Max(1, supportLookback))];

            long fiveMinuteSupportLow = previousFiveMinuteBars
                .Where(bar => bar.Low > 0)
                .Select(bar => bar.Low)
                .DefaultIfEmpty(0)
                .Min();

            double averageVolume = previousOneMinuteBars.Count > 0
                ? previousOneMinuteBars.Average(bar => Math.Max(0, bar.Volume))
                : 0;
            double averageTradingValue = previousOneMinuteBars.Count > 0
                ? previousOneMinuteBars.Average(bar => Math.Max(0, bar.TradingValue))
                : 0;

            double volumeRatio = averageVolume > 0 ? one.Volume / averageVolume : 0;
            double tradingValueRatio = averageTradingValue > 0 ? one.TradingValue / averageTradingValue : 0;
            double upperTailRatio = CalculateUpperTailRatio(one);
            double buyRatio = realtimeFlow.BuyTradeVolumeRatio60s;

            bool oneMinuteWeak = one.Ma5 > 0 && one.Close < one.Ma5;
            bool fiveMinuteWeak = five.Ma5 > 0 && five.Close < five.Ma5;
            bool fiveMinuteSupportBroken = fiveMinuteSupportLow > 0 && one.Close <= fiveMinuteSupportLow;
            bool oneMinuteBearish = one.Open > 0 && one.Close < one.Open;
            bool highVolume = volumeRatio >= highVolumeMultiple;
            bool highTradingValue = tradingValueRatio >= highTradingValueMultiple;
            bool upperTail = upperTailRatio >= upperTailThreshold;
            bool marketSellPressure =
                realtimeFlow.TotalTradeVolume60s > 0 &&
                buyRatio > 0 &&
                buyRatio <= marketSellPressureBuyRatioThreshold;

            if (fiveMinuteSupportBroken && (highVolume || highTradingValue || marketSellPressure))
            {
                return Build(
                    normalizedMarket,
                    StrategyFiveMinuteOneMinuteSellTestAction.StopAll,
                    true,
                    100,
                    "FatalExit",
                    $"5m support broken with pressure: support {fiveMinuteSupportLow:N0}, close {one.Close:N0}",
                    one,
                    fiveMinuteSupportLow,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fiveMinuteSupportBroken,
                    oneMinuteBearish,
                    highVolume,
                    highTradingValue,
                    upperTail,
                    marketSellPressure,
                    volumeRatio,
                    tradingValueRatio,
                    upperTailRatio,
                    buyRatio);
            }

            if (fiveMinuteSupportBroken)
            {
                return Build(
                    normalizedMarket,
                    StrategyFiveMinuteOneMinuteSellTestAction.StopAll,
                    true,
                    100,
                    "FatalExit",
                    $"5m support broken: support {fiveMinuteSupportLow:N0}, close {one.Close:N0}",
                    one,
                    fiveMinuteSupportLow,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fiveMinuteSupportBroken,
                    oneMinuteBearish,
                    highVolume,
                    highTradingValue,
                    upperTail,
                    marketSellPressure,
                    volumeRatio,
                    tradingValueRatio,
                    upperTailRatio,
                    buyRatio);
            }

            if (fiveMinuteWeak && oneMinuteWeak && (highVolume || highTradingValue || marketSellPressure))
            {
                return Build(
                    normalizedMarket,
                    StrategyFiveMinuteOneMinuteSellTestAction.ProtectProfit,
                    false,
                    50,
                    "ProtectProfit",
                    "5m/1m weak with pressure",
                    one,
                    fiveMinuteSupportLow,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fiveMinuteSupportBroken,
                    oneMinuteBearish,
                    highVolume,
                    highTradingValue,
                    upperTail,
                    marketSellPressure,
                    volumeRatio,
                    tradingValueRatio,
                    upperTailRatio,
                    buyRatio);
            }

            if (oneMinuteWeak && oneMinuteBearish && upperTail && (highVolume || highTradingValue))
            {
                return Build(
                    normalizedMarket,
                    StrategyFiveMinuteOneMinuteSellTestAction.ScaleOut,
                    false,
                    30,
                    "ScaleOut",
                    "1m bearish upper-tail expansion",
                    one,
                    fiveMinuteSupportLow,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fiveMinuteSupportBroken,
                    oneMinuteBearish,
                    highVolume,
                    highTradingValue,
                    upperTail,
                    marketSellPressure,
                    volumeRatio,
                    tradingValueRatio,
                    upperTailRatio,
                    buyRatio);
            }

            if (oneMinuteWeak || fiveMinuteWeak)
            {
                return Build(
                    normalizedMarket,
                    StrategyFiveMinuteOneMinuteSellTestAction.Watch,
                    false,
                    0,
                    "Warning",
                    oneMinuteWeak ? "1m weak, watch only" : "5m weak, watch only",
                    one,
                    fiveMinuteSupportLow,
                    oneMinuteWeak,
                    fiveMinuteWeak,
                    fiveMinuteSupportBroken,
                    oneMinuteBearish,
                    highVolume,
                    highTradingValue,
                    upperTail,
                    marketSellPressure,
                    volumeRatio,
                    tradingValueRatio,
                    upperTailRatio,
                    buyRatio);
            }

            return Build(
                normalizedMarket,
                StrategyFiveMinuteOneMinuteSellTestAction.Hold,
                false,
                0,
                "ObserveOnly",
                "5m/1m sell test hold",
                one,
                fiveMinuteSupportLow,
                oneMinuteWeak,
                fiveMinuteWeak,
                fiveMinuteSupportBroken,
                oneMinuteBearish,
                highVolume,
                highTradingValue,
                upperTail,
                marketSellPressure,
                volumeRatio,
                tradingValueRatio,
                upperTailRatio,
                buyRatio);
        }

        private static StrategyFiveMinuteOneMinuteSellTestEvaluation Build(
            string market,
            StrategyFiveMinuteOneMinuteSellTestAction action,
            bool isFullExitCandidate,
            int sellWeightPercent,
            string severity,
            string reason,
            StrategyMinuteBar one,
            long fiveMinuteSupportLow,
            bool oneMinuteWeak,
            bool fiveMinuteWeak,
            bool fiveMinuteSupportBroken,
            bool oneMinuteBearish,
            bool oneMinuteHighVolume,
            bool oneMinuteHighTradingValue,
            bool oneMinuteUpperTail,
            bool marketSellPressure,
            double oneMinuteVolumeRatio,
            double oneMinuteTradingValueRatio,
            double upperTailRatio,
            double marketBuyRatio60s)
        {
            return new(
                true,
                action,
                action is StrategyFiveMinuteOneMinuteSellTestAction.ScaleOut
                    or StrategyFiveMinuteOneMinuteSellTestAction.ProtectProfit
                    or StrategyFiveMinuteOneMinuteSellTestAction.StopAll,
                isFullExitCandidate,
                sellWeightPercent,
                severity,
                reason,
                market,
                DateTime.Now,
                one.Close,
                fiveMinuteSupportLow,
                oneMinuteWeak,
                fiveMinuteWeak,
                fiveMinuteSupportBroken,
                oneMinuteBearish,
                oneMinuteHighVolume,
                oneMinuteHighTradingValue,
                oneMinuteUpperTail,
                marketSellPressure,
                oneMinuteVolumeRatio,
                oneMinuteTradingValueRatio,
                upperTailRatio,
                marketBuyRatio60s);
        }

        private static double CalculateUpperTailRatio(StrategyMinuteBar bar)
        {
            long high = bar.High;
            long low = bar.Low;
            if (high <= 0 || low <= 0 || high <= low)
                return 0;

            long bodyTop = Math.Max(bar.Open, bar.Close);
            long upperTail = Math.Max(0, high - bodyTop);
            return upperTail / (double)(high - low);
        }

        private static string NormalizeMarket(string market) =>
            string.Equals((market ?? string.Empty).Trim(), "NXT", StringComparison.OrdinalIgnoreCase)
                ? "NXT"
                : "KRX";
    }
}
