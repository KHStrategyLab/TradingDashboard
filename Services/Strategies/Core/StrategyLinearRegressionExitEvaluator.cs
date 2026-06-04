using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingDashboard.Services.Strategies
{
    public sealed record StrategyLinearRegressionExitEvaluation(
        bool IsReady,
        bool FastCrossDown,
        bool SlowCrossDown,
        bool ShouldExit,
        long CurrentClose,
        double FastLine,
        double SlowLine,
        string Reason)
    {
        public static StrategyLinearRegressionExitEvaluation Empty(string reason) =>
            new(false, false, false, false, 0, 0, 0, reason);
    }

    public static class StrategyLinearRegressionExitEvaluator
    {
        public static StrategyLinearRegressionExitEvaluation Evaluate(
            IReadOnlyList<StrategyMinuteBar> bars,
            int fastPeriod = 50,
            int slowPeriod = 100)
        {
            if (bars.Count < Math.Max(fastPeriod * 2, slowPeriod * 2))
                return StrategyLinearRegressionExitEvaluation.Empty("linear regression bars not enough");

            double[] closes = [.. bars.Select(bar => (double)bar.Close)];
            double?[] fastA1 = CalculateRegressionSeries(closes, fastPeriod);
            double?[] fastA2 = CalculateRegressionSeries(fastA1, fastPeriod);
            double?[] slowA3 = CalculateRegressionSeries(closes, slowPeriod);
            double?[] slowA4 = CalculateRegressionSeries(slowA3, slowPeriod);

            int currentIndex = bars.Count - 1;
            int previousIndex = bars.Count - 2;

            if (!TryResolveLine(fastA1, fastA2, currentIndex, out double fastLine) ||
                !TryResolveLine(fastA1, fastA2, previousIndex, out double previousFastLine) ||
                !TryResolveLine(slowA3, slowA4, currentIndex, out double slowLine) ||
                !TryResolveLine(slowA3, slowA4, previousIndex, out double previousSlowLine))
            {
                return StrategyLinearRegressionExitEvaluation.Empty("linear regression line not ready");
            }

            double currentClose = closes[currentIndex];
            double previousClose = closes[previousIndex];
            bool fastCrossDown = previousClose >= previousFastLine && currentClose < fastLine;
            bool slowCrossDown = previousClose >= previousSlowLine && currentClose < slowLine;
            bool shouldExit = fastCrossDown || slowCrossDown;
            string reason = shouldExit
                ? $"linear regression crossdown: fast {(fastCrossDown ? "Y" : "N")}, slow {(slowCrossDown ? "Y" : "N")}, close {currentClose:N0}"
                : $"linear regression alive: close {currentClose:N0}, fast {fastLine:N0}, slow {slowLine:N0}";

            return new StrategyLinearRegressionExitEvaluation(
                true,
                fastCrossDown,
                slowCrossDown,
                shouldExit,
                bars[currentIndex].Close,
                fastLine,
                slowLine,
                reason);
        }

        public static string ToProgressLabel(StrategyLinearRegressionExitEvaluation evaluation)
        {
            if (!evaluation.IsReady)
                return evaluation.Reason;

            string state = evaluation.ShouldExit ? "LR exit" : "LR alive";
            return $"{state} C {evaluation.CurrentClose:N0} / F {evaluation.FastLine:N0} / S {evaluation.SlowLine:N0}";
        }

        private static bool TryResolveLine(
            IReadOnlyList<double?> first,
            IReadOnlyList<double?> second,
            int index,
            out double line)
        {
            line = 0;
            if (index < 0 ||
                index >= first.Count ||
                index >= second.Count ||
                !first[index].HasValue ||
                !second[index].HasValue)
            {
                return false;
            }

            double eq = first[index]!.Value - second[index]!.Value;
            line = first[index]!.Value + eq;
            return true;
        }

        private static double?[] CalculateRegressionSeries(IReadOnlyList<double> source, int period)
        {
            double?[] result = new double?[source.Count];
            for (int index = period - 1; index < source.Count; index++)
            {
                double[] window = new double[period];
                for (int offset = 0; offset < period; offset++)
                    window[offset] = source[index - period + 1 + offset];

                result[index] = CalculateRegressionEndpoint(window);
            }

            return result;
        }

        private static double?[] CalculateRegressionSeries(IReadOnlyList<double?> source, int period)
        {
            double?[] result = new double?[source.Count];
            for (int index = period - 1; index < source.Count; index++)
            {
                double[] window = new double[period];
                bool ready = true;
                for (int offset = 0; offset < period; offset++)
                {
                    double? value = source[index - period + 1 + offset];
                    if (!value.HasValue)
                    {
                        ready = false;
                        break;
                    }

                    window[offset] = value.Value;
                }

                if (ready)
                    result[index] = CalculateRegressionEndpoint(window);
            }

            return result;
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
    }
}
