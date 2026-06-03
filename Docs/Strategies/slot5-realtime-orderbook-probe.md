# Slot 5 Realtime Order-Book Probe

This file is UTF-8.

## Rule

Slot 5 uses realtime trade flow and order-book support as a final gate.

- `0B` stock trade realtime remains registered as a candidate batch.
- `0D/0H` group `901` is reserved for the selected stock order-book UI.
- Slot 5 may request a small strategy order-book probe through group `902` only when its minute/price flow is already near `PrepareBuy` and it is waiting for `0D` freshness/support.
- The probe group is capped to a small candidate set and is cleared on WebSocket reconnect.
- `ApplyRealtimeHogaItem` updates strategy order-book snapshots for probe candidates without changing the visible order-book UI unless the item is the selected stock.

## Intent

Do not register all candidates for order-book realtime by default.

The program should keep the realtime load small:

1. Track all active candidates with `0B`.
2. Let the strategy minute ledger and 0B flow find near-buy candidates.
3. Register `0D/0H` probe only for candidates that are close enough to need order-book confirmation.
4. Feed the probe result back into `StrategyRealtimeFlowSnapshot`.

## Safety

The probe is a confirmation input only. It must not submit orders directly.

Orders still flow through:

`StrategySlot.Evaluate` -> `ProcessStrategySignalAlerts` -> `RiskGuard` -> `KiwoomTradingClient`

## Debug Snapshot

Strategy debug snapshots include `OrderBookProbe`.

This section records:

- whether the probe request code is registered,
- last `0D` order-book receive/log time,
- fresh/stale state,
- best ask/bid,
- total ask/bid quantity,
- bid quantity ratio.

This is for inspection only. It must not change strategy decisions by itself.
