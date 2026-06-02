# LeaderHistory Operating Rules

This document fixes the LeaderHistory direction decided on 2026-06-02.

## Purpose

LeaderHistory is the lightweight first-priority stock universe.

The program should load recent leaders from file first. Intraday condition-search entries may appear on screen, but they are not automatically saved as leaders during the session.

## Active Storage

Active leaders are stored in:

```text
Storage/LeaderHistory/active_leaders.json
Storage/LeaderHistory/active_leaders.csv
```

Only recent leaders needed for current operation stay in this file.

Current active window:

```text
recent 6 trading days
```

If the active file is missing, empty, or stale, the program may rebuild it from recent KRX daily data. This is a recovery path so trading does not stop after the program has been unused for several days.

Manual rebuild command:

```text
dotnet run -- --leader-history-rebuild
```

CandidateLedger promotion command:

```text
dotnet run -- --leader-history-promote-candidates
```

The promotion command reads `Storage/CandidateLedger/active_candidates.json` and writes `Storage/LeaderHistory/active_leaders.json/csv`. It keeps the older DataStore direct rebuild command as a fallback/recovery path.

## Archive Storage

Historical leader records are stored separately:

```text
Storage/LeaderHistory/archive/
```

Archive records are retained as research assets. They are not deleted just because a stock leaves the active list.

Active removal means "do not use as today's first-priority candidate", not "delete history".

## Required Save Gate

LeaderHistory save gate is intentionally broad.

Required conditions:

```text
Market = KRX
TradingValue >= 100,000,000,000
ChangeRate >= 25%
```

These conditions are used for after-close save, next-morning recovery save, startup recovery save, and default automatic rebuild.

Intraday condition-search new entries are screen-only by default. They can be watched, alerted, and researched, but they are not saved to LeaderHistory until after-close or recovery processing confirms them on KRX daily data.

Manual add is allowed and should be marked with `Source = Manual`.

## Optional Tags

The following values are optional tags, not blocking conditions:

```text
BollingerUpperBreak
PrevHighPlus10
CloseLocationPercent
UpperTailPercent
NxtClose
```

Do not block LeaderHistory save just because these tags are false or missing.

`PrevHighPlus10` remains useful as a strength tag. A separate N-wave check is not required at the storage gate because second-wave patterns should not be blocked at entry to the leader file.

## KRX / NXT Rule

LeaderHistory is saved on KRX daily data.

NXT close can be stored only as a reference value. It must not replace KRX base candle values or KRX leader save criteria.

## Runtime Rule

Use order:

```text
1. Load active_leaders.json
2. If missing or stale, rebuild recent 6 trading days from KRX daily data
3. Show intraday condition-search entries on screen only
4. Save confirmed leaders after close or by recovery/manual path
5. Move expired active leaders out of active use, but keep archive records
```

## Current Fields

Recommended active record fields:

```text
Code
Name
Market
BaseDate
Volume
TradingValue
ChangeRate
DailyRsi14
CloseLocationPercent
UpperTailPercent
TurnoverRate
KrxClose
NxtClose
BollingerUpperBreak
PrevHighPlus10
Source
Status
SavedAt
ExpiresAfterTradingDays
ManualDiscarded
```

`TurnoverRate` is optional. The strategy must still run when it is missing.

## Current Quality Grade

The first implementation writes a provisional quality score and grade.

The score uses currently available fields first:

```text
TradingValue
ChangeRate
PrevHighPlus10
CloseLocationPercent
UpperTailPercent
BollingerUpperBreak
```

Reserved fields are kept for later expansion:

```text
MarketCap
MarketCapClass
ValueToMarketCapPercent
TradingValueToMarketCapPercent
TurnoverRate
```

`TradingValueToMarketCapPercent` means daily trading value divided by market cap. `ValueToMarketCapPercent` is kept as a compatibility alias for the same value. `TurnoverRate` remains share-volume based and should not be mixed with market-cap ratio.

`MarketCapClass` is a coarse helper tag: `MegaCap`, `LargeCap`, `MidLargeCap`, `MidCap`, `SmallCap`, or `Unknown`.

When market cap is missing, the scorer gives a neutral placeholder instead of blocking the leader record. Later, when market cap or listed-share data is available, the same record shape can distinguish absolute market leaders from stock-relative leaders.

The current scorer also gives provisional helper weight to market-cap scale and turnover. This is only ranking support; it is not a save gate.

`DailyRsi14` is carried from CandidateLedger when promoted. Direct rebuild also calculates it from KRX daily close values. It is currently informational and not a score component.

Current provisional score components:

```text
TradingValue                 max 22
MarketCap scale              max 8
TradingValue/MarketCap ratio max 18
TurnoverRate                 max 7
InvestorNetBuyScore          max 3
ChangeRate                   max 12
PrevHighPlus10               max 12
CloseLocationPercent         max 8
UpperTailPercent             max 8
BollingerUpperBreak          max 5
```

A++ should remain rare. If too many rows become A++, the weights should be tightened before strategy slots depend on the grade.

Current grade labels:

```text
A++
A+
A
B
C
Exclude
```
