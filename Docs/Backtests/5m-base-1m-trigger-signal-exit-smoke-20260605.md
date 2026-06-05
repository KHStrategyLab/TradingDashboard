# 5m Base / 1m Trigger Signal-Exit Smoke Check

Date: 2026-06-05

This smoke check verifies that the 5-minute base candle / 1-minute trigger backtest path can generate paired BUY/SELL signals with market separation.
The generated Run folder was deleted after inspection. This report is not a strategy performance verdict.

## Command

```powershell
dotnet run --project .\TradingDashboard.csproj -- --backtest-small-base-center-5m-1m-signal-exit
```

## Entry/Exit Shape

- Strategy: `SMALL_BASE_MA60_CENTER_PULLBACK`
- Entry frame: 5-minute base candle, 1-minute trigger
- Base threshold: 0.8% body rise and 500,000,000 KRW base-bar trading value
- Exit rule: `SIGNAL_EXIT_1M_MA5_BASE_LOW_15M_STRUCTURE_MAX180`

Representative exit conditions:

- 5-minute base low break
- 15-minute MA5 flow damage
- base-frame MA5 structural weakness
- 1-minute MA5 down-cross
- max 180-minute holding

Fixed N% loss, break-even recovery, and N% trailing exits are intentionally excluded.
The purpose is to test whether the sell signal is structurally usable, not whether a fixed-percent stop can cut loss.

## Smoke Result

Observed before deleting the generated Run folder from the structural-exit smoke pass:

- Signals: 266
- Trades: 133
- BUY/SELL pairing: matched
- KRX signals: 95 BUY / 95 SELL
- NXT signals: 38 BUY / 38 SELL
- RunMode: `SOR_ON`
- MarketMode: `MARKET_SPLIT`
- ExitRuleCode: `SIGNAL_EXIT_1M_MA5_BASE_LOW_15M_STRUCTURE_MAX180`
- WinRate: 39.10%
- Expectancy: -0.0504%
- AvgHoldingMinutes: 4.80
- Build: 0 warnings / 0 errors

The smoke pass confirmed that the path runs end-to-end and preserves KRX/NXT market labels.
Profitability is intentionally not judged from this smoke run.

## Next Comparison Candidates

Do not mix these into the same smoke result. Add as separate `ExitRuleCode` variants when needed:

- linear regression exit: `CROSSDOWN(C, VL) OR CROSSDOWN(C, VL1)`
- Heikin-Ashi reversal exit
- volume-expansion sell pressure exit
- 1-minute MA5/MA60 deterioration exit
