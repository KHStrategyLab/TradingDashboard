using System;

namespace TradingDashboard.Services.Trading
{
    public static class KiwoomOrderPriceRules
    {
        public static long ApplyTickOffset(long currentPrice, int tickOffset)
        {
            if (currentPrice <= 0)
                throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price must be positive.");

            long price = NormalizeToTick(currentPrice);
            int remaining = Math.Abs(tickOffset);
            int direction = tickOffset < 0 ? -1 : 1;

            while (remaining-- > 0)
            {
                long next = direction > 0
                    ? price + GetTickSize(price)
                    : NormalizeToTick(price - 1);
                if (next <= 0)
                    throw new ArgumentOutOfRangeException(nameof(tickOffset), "Tick offset produced a non-positive order price.");

                price = next;
            }

            return price;
        }

        public static long NormalizeToTick(long price)
        {
            if (price <= 0)
                throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");

            long tick = GetTickSize(price);
            return price / tick * tick;
        }

        public static long GetTickSize(long price)
        {
            if (price <= 0)
                throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");

            return price switch
            {
                < 2_000 => 1,
                < 5_000 => 5,
                < 20_000 => 10,
                < 50_000 => 50,
                < 200_000 => 100,
                < 500_000 => 500,
                _ => 1_000
            };
        }
    }
}
