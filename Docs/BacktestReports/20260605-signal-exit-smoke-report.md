# Signal-to-Signal Backtest Smoke Report

Date: 2026-06-05

This file is UTF-8.

## Purpose

This smoke test checks whether the backtest engine can run a real entry-to-exit flow:

```text
BUY signal
-> open virtual position
-> wait for SELL signal
-> close virtual position
-> write signals/trades/summaries
```

The source DataStore was not deleted, reset, or rewritten by this test.
The run only created a new folder under `Storage/Backtests/Runs/`.

## Command

```powershell
dotnet run --project .\TradingDashboard.csproj -- --backtest-ten-pullback-five-breakout-signal-exit
```

## Run

RunId:

```text
SORON_ten_ma60_pullback_five_high20_signal_exit180_20260605110635
```

Output:

```text
Storage/Backtests/Runs/SORON_ten_ma60_pullback_five_high20_signal_exit180_20260605110635/
```

Strategy:

```text
TEN_MA60_PULLBACK_FIVE_HIGH20_BREAK
```

Exit rule:

```text
SIGNAL_EXIT_1M_MA5_5M_BASE_15M_TRAIL_MAX180
```

## Exit Rules In This First Version

The first version is intentionally simple. It closes the whole virtual position on the first matching full-exit signal:

- hard stop at `-1.2%`
- 5-minute high20/base low break
- 15-minute MA5 flow damage
- 5-minute MA5 weakness while position is losing
- profit protection trail after `+1%` and high drawdown `>= 1%`
- 1-minute MA5 down-cross while profitable
- break-even recovery after at least `-1%` drawdown
- maximum holding time `180m`

Partial scale-out is intentionally not modeled yet.

## Result Summary

| Run | Market | Signals | Trades | WinRate | Expectancy | Avg Hold | Max Consecutive Losses |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| fixed 30m | MIXED | 4,038 | 4,038 | 40.14% | -0.1242% | 30.00m | 17 |
| fixed 180m | MIXED | 1,572 | 1,572 | 38.61% | -0.4780% | 180.00m | 23 |
| signal exit | MIXED | 2,814 | 1,407 | 40.94% | -0.1811% | 29.87m | 10 |
| signal exit | KRX | 2,282 | 1,141 | 39.88% | -0.2195% | 32.90m | 11 |
| signal exit | NXT | 532 | 266 | 45.49% | -0.0163% | 16.88m | 7 |

## Interpretation

The signal-to-signal path works:

- `BUY` and `SELL` rows are paired: 1,407 buys and 1,407 sells.
- `MarketMode` is preserved as `MARKET_SPLIT`.
- KRX and NXT are separated in both signals and trades.
- The run completed without modifying the reusable source DataStore.

The result is not yet good enough as a trading rule:

- Mixed expectancy is still negative.
- Signal exit reduced max consecutive losses compared with fixed 30m and fixed 180m.
- Signal exit did not improve expectancy versus fixed 30m.
- NXT is close to flat, while KRX is still pulling the total result down.

Main read:

```text
The exit signal is reducing risk, but it is probably cutting winners too early or entering too many weak KRX cases.
```

## Next Work

Do not rewrite the DataStore.

Next iteration should compare exit rule variants only:

1. Keep the same entry signal.
2. Split reports by KRX/NXT.
3. Compare:
   - current signal exit
   - no 1m MA5 profit-protection exit
   - trailing-only after `+1%`
   - 5m base-low/15m damage hard exits only
4. Check whether KRX needs a stricter entry filter instead of a different exit.
5. Keep NXT and KRX behavior separate; do not average them too early.

## Guardrail

This backtest is a research output only.
It must not be connected to `Live Orders`, order API calls, or RiskGuard bypass logic.
