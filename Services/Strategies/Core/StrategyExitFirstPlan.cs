using System;
using System.Collections.Generic;

namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyExitFirstPlan(
        long EntryPrice,
        long StopPrice,
        long TargetPrice,
        double StopRiskPercent,
        double RewardRiskRatio,
        IReadOnlyList<string> NoBuyReasons)
    {
        public bool HasStop => StopPrice > 0 && EntryPrice > StopPrice;

        public bool HasTarget => TargetPrice > EntryPrice;

        public bool IsTradable => NoBuyReasons.Count == 0;

        public static StrategyExitFirstPlan Empty(string reason) =>
            new(0, 0, 0, 0, 0, [reason]);
    }

    public static class StrategyExitFirstPlanner
    {
        private const double DefaultTargetRewardMultiple = 1.8;

        public static StrategyExitFirstPlan Build(
            long entryPrice,
            long stopPrice,
            long targetPrice = 0,
            double minRewardRiskRatio = 1.5,
            double minStopRiskPercent = 0.25,
            double maxStopRiskPercent = 3.0,
            double fallbackTargetRewardMultiple = DefaultTargetRewardMultiple)
        {
            List<string> noBuyReasons = [];

            if (entryPrice <= 0)
                noBuyReasons.Add("no entry price");
            if (stopPrice <= 0)
                noBuyReasons.Add("no stop anchor");

            if (entryPrice <= 0 || stopPrice <= 0)
                return new StrategyExitFirstPlan(entryPrice, stopPrice, targetPrice, 0, 0, noBuyReasons);

            if (stopPrice >= entryPrice)
                noBuyReasons.Add("stop not below entry");

            double risk = Math.Max(0, entryPrice - stopPrice);
            double stopRiskPercent = entryPrice > 0 ? risk / entryPrice * 100.0 : 0;
            if (stopRiskPercent > 0 && stopRiskPercent < minStopRiskPercent)
                noBuyReasons.Add("stop risk too narrow");
            if (stopRiskPercent > maxStopRiskPercent)
                noBuyReasons.Add("stop risk too wide");

            if (targetPrice <= entryPrice && risk > 0)
                targetPrice = entryPrice + (long)Math.Round(risk * fallbackTargetRewardMultiple, MidpointRounding.AwayFromZero);

            if (targetPrice <= entryPrice)
                noBuyReasons.Add("target room missing");

            double reward = Math.Max(0, targetPrice - entryPrice);
            double rewardRiskRatio = risk > 0 ? reward / risk : 0;
            if (rewardRiskRatio > 0 && rewardRiskRatio < minRewardRiskRatio)
                noBuyReasons.Add("reward/risk too low");

            return new StrategyExitFirstPlan(
                entryPrice,
                stopPrice,
                targetPrice,
                stopRiskPercent,
                rewardRiskRatio,
                noBuyReasons);
        }

        public static StrategyExitFirstPlan FromMa60Breakout(
            StrategyMinuteBreakoutCheck minuteCheck,
            double maxStopRiskPercent = 3.0)
        {
            long stopPrice = minuteCheck.Ma60 > 0
                ? (long)Math.Round(minuteCheck.Ma60, MidpointRounding.AwayFromZero)
                : 0;

            return Build(
                minuteCheck.SignalPrice,
                stopPrice,
                maxStopRiskPercent: maxStopRiskPercent);
        }

        public static string FormatSummary(StrategyExitFirstPlan plan)
        {
            string entry = plan.EntryPrice > 0 ? plan.EntryPrice.ToString("N0") : "-";
            string stop = plan.StopPrice > 0 ? plan.StopPrice.ToString("N0") : "-";
            string target = plan.TargetPrice > 0 ? plan.TargetPrice.ToString("N0") : "-";
            string rr = plan.RewardRiskRatio > 0 ? $"{plan.RewardRiskRatio:0.##}R" : "-";
            string risk = plan.StopRiskPercent > 0 ? $"{plan.StopRiskPercent:0.##}%" : "-";
            return $"exit-first entry {entry} / stop {stop} / target {target} / risk {risk} / RR {rr}";
        }
    }
}
