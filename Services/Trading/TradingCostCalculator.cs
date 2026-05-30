using System;
using TradingDashboard.Models;

namespace TradingDashboard.Services.Trading
{
    public sealed class TradingCostCalculator
    {
        private readonly TradingCostSettings _settings;

        public TradingCostCalculator(TradingCostSettings? settings)
        {
            _settings = settings ?? new TradingCostSettings();
        }

        public TradingCostRate ResolveRate(string market)
        {
            string value = (market ?? string.Empty).Trim().ToUpperInvariant();
            return value switch
            {
                "NXT" => Merge(_settings.Default, _settings.Nxt),
                "SOR" => Merge(_settings.Default, _settings.Sor),
                "KRX" => Merge(_settings.Default, _settings.Krx),
                _ => Merge(_settings.Default, _settings.Krx)
            };
        }

        public TradingCostEstimate Estimate(long buyAmount, long evaluationAmount, string market)
        {
            TradingCostRate rate = ResolveRate(market);
            long buyCommission = EstimateCost(buyAmount, rate.CommissionRate);
            long sellCommission = EstimateCost(evaluationAmount, rate.CommissionRate);
            long sellTax = EstimateCost(evaluationAmount, rate.SellTaxRate);
            long netEvaluationAfterSellCost = Math.Max(0, evaluationAmount - sellCommission - sellTax);

            return new TradingCostEstimate(
                buyCommission,
                sellCommission,
                sellTax,
                buyCommission + sellCommission + sellTax,
                netEvaluationAfterSellCost,
                rate.CommissionRate,
                rate.SellTaxRate);
        }

        public static long EstimateCost(long amount, decimal rate)
        {
            if (amount <= 0 || rate <= 0)
                return 0;

            return (long)Math.Ceiling(amount * rate);
        }

        private static TradingCostRate Merge(TradingCostRate defaults, TradingCostRate specific)
        {
            return new TradingCostRate
            {
                CommissionRate = specific.CommissionRate > 0 ? specific.CommissionRate : defaults.CommissionRate,
                SellTaxRate = specific.SellTaxRate > 0 ? specific.SellTaxRate : defaults.SellTaxRate
            };
        }
    }

    public sealed record TradingCostEstimate(
        long BuyCommission,
        long SellCommission,
        long SellTax,
        long TotalEstimatedCost,
        long NetEvaluationAfterSellCost,
        decimal CommissionRate,
        decimal SellTaxRate);
}
