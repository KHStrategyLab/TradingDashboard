using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public static class StrategyProgressCalculator
    {
        public const double BuyFilledPercent = 70d;
        public const double ExitCompletePercent = 100d;

        public static StrategyProgressStep Step(string key, string label, bool isCompleted) =>
            new(key, label, isCompleted, false);

        public static StrategyProgressSnapshot Build(
            StrategySlotId slotId,
            string stateKey,
            string stateText,
            IReadOnlyList<StrategyProgressStep> buySteps,
            IReadOnlyList<StrategyProgressStep>? sellSteps = null,
            bool isOwned = false,
            bool isFinished = false,
            string levelText = "-",
            double strengthPercent = 0,
            string strengthLabel = "기준강도")
        {
            List<StrategyProgressStep> normalizedBuySteps = [.. buySteps];
            List<StrategyProgressStep> normalizedSellSteps = sellSteps == null ? [] : [.. sellSteps];

            int completedBuySteps = normalizedBuySteps.Count(step => step.IsCompleted);
            int completedSellSteps = normalizedSellSteps.Count(step => step.IsCompleted);
            int totalBuySteps = normalizedBuySteps.Count;
            int totalSellSteps = normalizedSellSteps.Count;

            double progressPercent;
            if (isFinished)
            {
                progressPercent = ExitCompletePercent;
            }
            else if (isOwned)
            {
                progressPercent = totalSellSteps == 0
                    ? BuyFilledPercent
                    : BuyFilledPercent + (completedSellSteps / (double)totalSellSteps * (ExitCompletePercent - BuyFilledPercent));
            }
            else
            {
                progressPercent = totalBuySteps == 0
                    ? 0
                    : completedBuySteps / (double)totalBuySteps * BuyFilledPercent;
            }

            progressPercent = Math.Clamp(progressPercent, 0, ExitCompletePercent);
            int currentIndex = ResolveCurrentIndex(normalizedBuySteps, normalizedSellSteps, isOwned, isFinished);
            List<StrategyProgressStep> steps = MarkCurrent([.. normalizedBuySteps, .. normalizedSellSteps], currentIndex);
            int totalSteps = steps.Count;
            int currentStep = totalSteps == 0 ? 0 : Math.Clamp(currentIndex + 1, 1, totalSteps);

            return new StrategyProgressSnapshot(
                slotId,
                stateKey,
                stateText,
                currentStep,
                totalSteps,
                progressPercent,
                levelText,
                Math.Clamp(strengthPercent, 0, 100),
                strengthLabel,
                steps);
        }

        private static int ResolveCurrentIndex(
            IReadOnlyList<StrategyProgressStep> buySteps,
            IReadOnlyList<StrategyProgressStep> sellSteps,
            bool isOwned,
            bool isFinished)
        {
            if (isFinished)
                return Math.Max(0, buySteps.Count + sellSteps.Count - 1);

            if (!isOwned)
            {
                int nextBuyStep = IndexOfFirstIncomplete(buySteps);
                return nextBuyStep >= 0 ? nextBuyStep : Math.Max(0, buySteps.Count - 1);
            }

            int nextSellStep = IndexOfFirstIncomplete(sellSteps);
            return nextSellStep >= 0
                ? buySteps.Count + nextSellStep
                : Math.Max(0, buySteps.Count + sellSteps.Count - 1);
        }

        private static int IndexOfFirstIncomplete(IReadOnlyList<StrategyProgressStep> steps)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].IsCompleted)
                    return i;
            }

            return -1;
        }

        private static List<StrategyProgressStep> MarkCurrent(IReadOnlyList<StrategyProgressStep> steps, int currentIndex)
        {
            List<StrategyProgressStep> result = [];
            for (int i = 0; i < steps.Count; i++)
            {
                StrategyProgressStep step = steps[i];
                result.Add(step with { IsCurrent = i == currentIndex });
            }

            return result;
        }
    }
}
