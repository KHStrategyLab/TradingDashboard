# Exit-First Risk Filter

## Purpose

TradingDashboard checks the exit plan before allowing a strategy signal to hand off to orders.

The rule is:

```text
Find the stop first.
Then check target room.
Then check reward/risk.
Only then allow the buy trigger.
```

This keeps the system aligned with the core principle:

```text
Automatic trading is a risk filter, not a buy button.
```

## Current Implementation

The common calculation lives in:

```text
Services/Strategies/Core/StrategyExitFirstPlan.cs
```

The planner calculates:

```text
EntryPrice
StopPrice
TargetPrice
StopRiskPercent
RewardRiskRatio
NoBuyReasons
```

If `NoBuyReasons` is not empty, the strategy may continue tracking but must not allow order handoff.

## Initial Rules

The first version uses these safeguards:

```text
Minimum reward/risk: 1.5R
Minimum stop risk: 0.25%
Maximum stop risk: normally 3.0%
Intraday scalp max stop risk: 2.5%
Fallback target: 1.8R from the stop distance
```

Slot 5 has its own fixed scalp target around `+1.8%`, so its reward/risk can block late or wide entries more aggressively.

## Connected Strategy Slots

The first pass is connected to:

```text
Slot 1: SOR 10m MA60 + 3m Breakout
Slot 2: SOR 15m MA60 + 5m Breakout
Slot 3: SOR 10m MA60 + 5m Breakout
Slot 5: Intraday 15m Base + 1m Trigger
Slot 6: Intraday 5m Base + 1m Stable
```

Each connected slot adds a progress step:

```text
exit-first RR
```

The debug snapshot now records:

```text
EntryPrice
StopPrice
TargetPrice
RewardRiskRatio
NoBuyReasons
```

This lets the user and Codex review the same numeric evidence without touching live orders.

## Locked Direction

This is a pre-order filter only.

Do not connect this directly to `KiwoomTradingClient`.
The only valid route remains:

```text
StrategySlot.Evaluate
-> ProcessStrategySignalAlerts
-> RiskGuard
-> KiwoomTradingClient
```

Future work can improve `TargetPrice` by using resistance, base price, and madi/waist functions. Until then, the fallback target keeps the filter conservative and easy to verify.
