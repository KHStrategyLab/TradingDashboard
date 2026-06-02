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

Manual fundamental enrich command:

```text
dotnet run -- --candidate-ledger-enrich-fundamentals
```

This command is for manual/after-close use. It does not run on realtime condition-enter events.

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
DailyRsi14
IsBollingerUpperBreak
PrevHighBreak
```

Fundamental and market-rank fields remain pending until a source is attached:

```text
MarketCap
MarketCapClass
ListedShares
FloatingShares
ValueToMarketCapPercent
TradingValueToMarketCapPercent
TurnoverRateByListedShares
TurnoverRateByFloatingShares
TradingValueRankInMarket
ChangeRateRankInMarket
TurnoverRankInMarket
```

Current fundamental source candidates:

```text
MarketCap:
  ka10095.atn_stk_infr.mac or calculated CurrentPrice * ListedShares

ListedShares:
  ka10100.listCount
  fallback: stkcnt, lst_stk_cnt, list_stkcnt

FloatingShares:
  ka10007.flo_stkcnt
  fallback: float_stkcnt, floating_shares, distb_stkcnt
```

Share unit normalization:

```text
raw value < 1,000,000  => treat as 1,000-share unit and multiply by 1000
raw value >= 1,000,000 => treat as 1-share unit
```

Market cap uses listed shares. Turnover prefers floating shares, then listed shares if floating shares are missing.

Market cap class is a coarse helper tag:

```text
MegaCap     = market cap 10T+
LargeCap    = market cap 3T+
MidLargeCap = market cap 1T+
MidCap      = market cap 300B+
SmallCap    = below 300B
```

Ratio naming:

```text
TradingValueToMarketCapPercent = TradingValue / MarketCap * 100
ValueToMarketCapPercent        = compatibility alias for the same value
TurnoverRateByListedShares     = Volume / ListedShares * 100
TurnoverRateByFloatingShares   = Volume / FloatingShares * 100
```

Trading-value-to-market-cap ratio and share turnover are separate concepts. Keep both when the data exists.

Daily RSI:

```text
DailyRsi14 = RSI(14) calculated from KRX daily close values through CandidateDate
```

This is a ledger reference field. It does not block CandidateLedger save.

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
