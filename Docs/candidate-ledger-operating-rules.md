# CandidateLedger Operating Rules

This document fixes the CandidateLedger direction decided on 2026-06-02.

## Purpose

CandidateLedger is the raw material room for candidate entries.

It records a stock as soon as it becomes a candidate, then leaves missing calculation fields as `Pending` until daily bars, fundamentals, or market ranking data are available.

CandidateLedger does not decide whether a stock is a real leader.

```text
CandidateLedger = raw candidate event and enrichment fields
LeaderHistory   = selected recent leaders after validation
DataStore       = reusable daily/minute source data
```

## Storage

```text
Storage/CandidateLedger/
  active_candidates.json
  active_candidates.csv
  archive/
  metadata/
```

Runtime files are local data and are not backed up to GitHub.

## Manual Rebuild

The current rebuild path uses existing KRX daily DataStore files and fills the latest 6 trading days with broad candidate rows.

```text
dotnet run -- --candidate-ledger-rebuild
```

Current rebuild gate:

```text
Market = KRX
TradingValue >= 100,000,000,000
ChangeRate >= 25%
```

This is not a score. It is only a raw candidate room initializer.

## Upsert Key

```text
Code + Market + CandidateDate + Source
```

The active file is updated by key. Existing source data is not deleted.

## Field Policy

CandidateLedger prepares fields before all data sources are ready.

Immediately available daily DataStore fields are filled:

```text
TodayOpen
TodayHigh
TodayLow
TodayClose
TodayVolume
TodayTradingValue
PrevOpen
PrevHigh
PrevLow
PrevClose
PrevVolume
PrevTradingValue
AvgVolume20D
AvgTradingValue20D
IsBollingerUpperBreak
PrevHighBreak
```

Fundamental and market-rank fields remain pending until a source is attached:

```text
MarketCap
ListedShares
FloatingShares
ValueToMarketCapPercent
TurnoverRateByListedShares
TurnoverRateByFloatingShares
TradingValueRankInMarket
ChangeRateRankInMarket
TurnoverRankInMarket
```

Score fields remain pending:

```text
SelectionScore
MarketLogicScore
BaseCandleQualityScore
LeaderScore
LiquidityScore
TurnoverScore
```

## Flow

```text
Candidate appears
-> CandidateLedger upsert
-> DataStore daily enrich/backfill
-> fundamentals and market rank enrich
-> score profiles calculated later
-> LeaderHistory promotion
```

## Rules

Do not calculate final leader score in CandidateLedger stage.

Do not overwrite or delete Backtest DataStore source data.

Do not connect CandidateLedger to live order execution.

Use KRX daily data for the initial candidate rebuild. NXT/SOR values may be added later as reference fields, but they must not replace KRX base daily values.
