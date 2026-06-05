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

## Exit Break Tracking

After a position is owned, the same strategy progress bar must keep tracking whether the exit base is still alive.

The common evaluator lives in:

```text
Services/Strategies/Core/StrategyExitBreakEvaluator.cs
```

It translates the lecture terms into numeric states:

```text
None             = base alive
FirstBreak       = first close below the exit base
TrapCandidate    = low-volume first break that may recover
Recovered        = close recovered above the base after a break
SecondBreak      = repeated break of the same base
StrongBreak      = base break with volume expansion or repeated failure
ReboundRejected  = price rebounded near the base but closed below it again
```

Connected slots show this in the sell-side progress step:

```text
stop
```

Current policy:

```text
ShouldExit = true for StrongBreak, SecondBreak, ReboundRejected
```

This is still a progress/diagnostic signal only.
It does not directly submit a sell order.
Live sell order handoff must later pass through the strategy position book, RiskGuard, quantity ownership, and order journal.

## Staged Profit And Stop Exit

The staged exit rule set is a separate post-entry operating evaluator.

It lives in:

```text
Services/Strategies/Core/StrategyStagedExitEvaluator.cs
```

Its operating principle is:

```text
Scale out profits.
Cut losses as a full exit.
Use 1m weakness as early warning.
Use 5m base damage as the final stop.
Use 15m flow damage as a full-exit condition.
```

Current rule summary:

```text
5m base low break -> full structural exit
15m weak          -> full structural exit
5m weak           -> full structural exit
1m weak           -> partial structural warning/reduction
closing time      -> full exit

Fixed N% loss, fixed N% target, break-even recovery, and high-to-low N% trailing
are excluded from prepared sell formulas.
```

This evaluator is not wired to live orders yet.
It requires position state such as post-buy high, minimum profit rate, and completed scale-out flags.
Those values must be owned by the position ledger or backtest engine before live handoff is considered.

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
