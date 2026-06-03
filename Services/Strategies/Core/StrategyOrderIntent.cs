using System.Collections.Generic;

namespace TradingDashboard.Services.Strategies
{
    public enum StrategyOrderIntentAction
    {
        None,
        Watch,
        PrepareBuy,
        BuyReady,
        BuyNow,
        PrepareSell,
        SellNow
    }

    public sealed record StrategyOrderIntent(
        StrategyOrderIntentAction Action,
        bool AllowsOrderHandoff,
        string Reason,
        long EntryPrice = 0,
        long StopPrice = 0,
        long TargetPrice = 0,
        double RewardRiskRatio = 0,
        IReadOnlyList<string>? NoBuyReasons = null)
    {
        public static StrategyOrderIntent None(string reason = "") =>
            new(StrategyOrderIntentAction.None, false, reason);

        public static StrategyOrderIntent Watch(string reason) =>
            new(StrategyOrderIntentAction.Watch, false, reason);

        public static StrategyOrderIntent PrepareBuy(string reason, IReadOnlyList<string>? noBuyReasons = null) =>
            new(StrategyOrderIntentAction.PrepareBuy, false, reason, NoBuyReasons: noBuyReasons);

        public static StrategyOrderIntent BuyNow(
            string reason,
            long entryPrice,
            long stopPrice = 0,
            long targetPrice = 0,
            double rewardRiskRatio = 0) =>
            new(
                StrategyOrderIntentAction.BuyNow,
                true,
                reason,
                entryPrice,
                stopPrice,
                targetPrice,
                rewardRiskRatio,
                []);
    }
}
