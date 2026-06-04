# Backtest Data Management Principles

This document is written and maintained as UTF-8.

## Purpose

The backtest machine is not an automatic live-buy feature.

Its purpose is to build a verification lab before live strategy implementation:

```text
Keep existing data
+ add/update today's bars
+ download only missing required ranges
+ run many temporary strategies on the same dataset
+ keep only conditions that survive by profit and risk metrics
```

Data is a reusable asset. Strategies and exit rules are repeatable experiment conditions over that data.

## Non-Negotiable Rules

- Do not connect this workflow to realtime automatic buy logic.
- Do not generate live orders.
- Do not delete or reset existing daily or minute backtest data.
- Do not redownload daily or minute data just because a strategy or exit rule changed.
- Do not store duplicate rows for the same `Code + Market + Date` daily key.
- Do not store duplicate rows for the same `Code + Market + Minute + BucketTime` minute key.
- Do not treat intraday `Provisional` data as final confirmed backtest data.
- Do not download all minute bars for two months before verifying base candles.

## Storage Layout

Use a persistent data store and separate run results:

```text
Storage/Backtests/
  DataStore/
    daily/
    minute/
    base_candles/
    metadata/
  Runs/
    {RunId}/
      signals.csv
      trades.csv
      summaries.json
      strategy_comparison.csv
```

`DataStore` is the reusable source data store.

`Runs/{RunId}` is the output of a specific strategy/exit-rule execution.

Changing a strategy or exit rule creates a new run result, not a new data download.

## Data Status

Daily and minute rows may carry a status:

```text
Provisional = intraday temporary data
Confirmed = post-close confirmed data
Downloaded = downloaded and stored data
Verified = used by base-candle or strategy verification
```

Confirmed backtest reports use `Confirmed` data by default.

`Provisional` data may be used for intraday observation, candidate tracking, and pre-checks, but it must be marked separately or excluded from final reports.

## Upsert Keys

Daily row key:

```text
Code + Market + Date
```

Minute row key:

```text
Code + Market + Minute + BucketTime
```

If the same key already exists, update it in place.

Do not append duplicate rows.

## Today's Bar Handling

When a candidate appears today, today's daily bar is added to `DataStore`.

During the session, today's daily bar is stored as `Provisional`.

After market close, query the daily bar again, update the same row, and mark it `Confirmed`.

This must be an upsert, not a data reset.

## Download Policy

- Reuse stored recent daily bars when they already exist.
- Download only missing stocks, missing dates, or missing required minute windows.
- Verify base candles from stored daily bars before minute downloads.
- Do not download minute bars for stocks without verified base candles.
- For verified base-candle stocks, download only the required minute range after the base candle.
- Do not blindly download all two-month minute bars.

Recommended initial minute range:

```text
Start: first trading day after the base-candle date
End: up to five trading days after the base-candle date
Default minute frame: 10m
Optional expansion: 5m if a strategy needs it
```

## Base Candle Verification

Do not blindly trust search-condition candidates.

The program must re-verify base candles from stored recent daily bars plus today's newly added bar.

Intraday today's bar may become only a `Provisional` base-candle candidate.

After market close, re-check with `Confirmed` daily data and finalize whether it is a base candle.

Verified base candles are stored in:

```text
Storage/Backtests/DataStore/base_candles/
```

## Strategy Execution Policy

Data collection and strategy execution must stay separate.

Required flow:

```text
Build or update DataStore
-> load stored dataset
-> run many temporary strategies
-> compare many exit rules
-> save RunId-specific results
```

Strategy runners must operate only on the stored dataset.

Strategy execution must not trigger new downloads.

## Result Storage

Every strategy execution creates a new `RunId`.

Multiple strategies and exit rules must be runnable against the same dataset.

Backtest result rows must carry execution labels separately from the reusable source data.
The source dataset keeps the existing hierarchical file layout, while each run result records
`RunId`, `RunMode`, `MarketMode`, `Market`, and `Status` so a result from another PC cannot be
mistaken for a KRX-only run.

Required source-data keys:

```text
Daily:  Code + Market + Date
Minute: Code + Market + TimeFrame + BarTime
```

Current implementation note:

```text
TimeFrame = Minute
BarTime   = DateTime
```

Do not collapse KRX and NXT into one file or one result label. KRX/NXT can share a run, but the
result must say so through `MarketMode = MARKET_SPLIT` or another explicit run mode.

Required comparison fields:

```text
RunId
RunMode
MarketMode
Market
Status
StrategyCode
ExitRuleCode
SignalCount
TradeCount
WinRate
AvgProfit
AvgLoss
Expectancy
TotalProfit
MaxDrawdown
MAE
MFE
AvgHoldingMinutes
ConsecutiveLosses
FeeAdjustedProfit
SlippageAdjustedProfit
```

Results are stored under:

```text
Storage/Backtests/Runs/{RunId}/
```

## First Implementation Scope

The first implementation should stay small:

1. Load candidates from the existing watchlist/search-condition cache.
2. Download or reuse recent daily bars.
3. Upsert daily bars into `DataStore`.
4. Re-verify base candles from stored daily data.
5. Save verified base-candle rows.

Minute partial download, strategy execution, exit-rule comparison, and reports come after that.

Current first-step code locations:

```text
Models/BacktestCandidate.cs
Models/BacktestDailyBar.cs
Models/BacktestBaseCandle.cs
Models/BacktestDatasetUpdateSummary.cs
Models/BacktestMinuteBar.cs
Models/BacktestMinuteDataStoreSummary.cs
Models/BacktestSignalRow.cs
Models/BacktestTradeRow.cs
Models/BacktestRunSummary.cs
Services/Backtests/BacktestCandidateImportService.cs
Services/Backtests/BacktestConditionCandidateLoader.cs
Services/Backtests/BacktestDataStore.cs
Services/Backtests/DailyBaseCandleVerifier.cs
Services/Backtests/BacktestDatasetBuilder.cs
Services/Backtests/BacktestDailyDataStoreJob.cs
Services/Backtests/BacktestMinuteDataStoreJob.cs
Services/Backtests/BacktestRunStore.cs
Docs/backtest-datastore-runbook.md
```

These services are intentionally not wired to `Engine Start`, realtime strategy evaluation, or live order flow.

Candidate source priority:

```text
1. Kiwoom condition search 23, named 120일내_20퍼_500억
2. Config/watchlist_stock_cache.json via LoadFromWatchlistCache(...)
3. Excel/CSV/JSON import files via LoadCandidates(...)
```

The backtest candidate condition is stored separately from `Kiwoom.ConditionSeq01`:

```text
Backtest.CandidateConditionIndex = 23
Backtest.CandidateConditionName = 120일내_20퍼_500억
```

The current operator job is:

```text
BacktestDailyDataStoreJob.RunAsync(...)
```

It loads condition 23 candidates, writes the candidate metadata snapshot, updates/reuses daily bars, and verifies base-candle events. It remains disconnected from `Engine Start` and live order flow.

Operator command:

```powershell
dotnet run -- --backtest-daily-datastore
```

The minute DataStore operator command is:

```powershell
dotnet run -- --backtest-minute-datastore
```

It uses verified base-candle rows only, keeps minute data under `Storage/Backtests/DataStore/minute/`, and upserts by code, market, minute interval, and candle time.

Run result storage is prepared under:

```text
Storage/Backtests/Runs/{RunId}/
```

The run store writes:

```text
signals.csv
trades.csv
summaries.json
strategy_comparison.csv
```

The first smoke-test strategy is `TEN_MA60_PULLBACK_FIVE_HIGH20_BREAK`.

It is run with:

```powershell
dotnet run -- --backtest-ten-pullback-five-breakout
dotnet run -- --backtest-ten-pullback-five-breakout-3h
```

It measures signal quality by holding for a same-day observation window and recording high/low range. The 30-minute command uses six 5-minute bars, and the 3-hour command uses thirty-six 5-minute bars. It is not connected to live trading.

The first-priority small-base test is `SMALL_BASE_MA60_CENTER_PULLBACK`.

It is run with:

```powershell
dotnet run -- --backtest-small-base-center-10m-3m
dotnet run -- --backtest-small-base-center-10m-3m-1m
dotnet run -- --backtest-small-base-center-15m-5m-1m
```

This test uses a completed 10-minute MA60 recovery candle as the small base, then checks a completed 3-minute centerline pullback rebound. Results are evaluated by risk to the small-base low and R multiple, not by win rate alone.

The 1-minute trigger variant keeps the 10-minute place and 3-minute support check, then uses a completed 1-minute bullish high-break candle as the final timing trigger.

Role split:

```text
Large bar = place and direction.
Middle bar = pullback and support.
One-minute bar = trigger.
Execution strength/speed = live confirmation.
```

Current variants:

```text
10m / 3m / 1m = aggressive
15m / 5m / 1m = stable
```
