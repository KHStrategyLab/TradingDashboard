# Backtest DataStore Runbook

This is the first implementation path for the backtest data store.

It must stay separate from realtime automatic buying.

## Current Scope

Step 1 builds only this path:

```text
candidate source
-> recent daily bars
-> daily DataStore upsert
-> base-candle re-verification
-> verified base-candle event store
```

No minute download, no strategy execution, no live order.

## Candidate Sources

Preferred candidate input is Kiwoom condition search number 23:

```text
Backtest.CandidateConditionIndex = 23
Backtest.CandidateConditionName = 120일내_20퍼_500억
```

Until the operator-only condition loader is wired, use the existing TradingDashboard watchlist/search-condition cache.

Use:

```text
Config/watchlist_stock_cache.json
BacktestCandidateImportService.LoadFromWatchlistCache(...)
```

This avoids manually preparing stock lists.

Do not change `Kiwoom.ConditionSeq01` for this. That setting is still used by the live dashboard/watchlist flow.

Excel/CSV import remains a fallback for one-off offline candidate files.

Required columns for a real candidate file:

```text
Code
Name
Market
CandidateDate
```

Minimum usable columns:

```text
Code
Name
```

Defaults:

```text
Market = KRX
CandidateDate = today
Source = EXCEL
StrategyCode = BASE_CANDLE
```

The sample file `120일내_20퍼_500억.xls` currently contains the search-condition description only, not stock rows. Use it as source-condition documentation, not as the primary candidate list.

## Source Condition

The current source condition is:

```text
Lookback: 0 bars ago through the last 120 daily bars
Trading value: at least one daily bar >= 50,000,000,000 KRW
Change rate: at least one daily bar >= +20%
```

The backtest verifier does not blindly trust this source condition.

It rechecks stored daily bars and stores every bar where both event criteria are met on the same daily candle as a base-candle event.

## DataStore Paths

```text
Storage/Backtests/DataStore/daily/{Code}_{Market}_daily.json
Storage/Backtests/DataStore/base_candles/verified_base_candles.json
Storage/Backtests/DataStore/metadata/{SourceName}.json
```

These files are local runtime data and are ignored by Git.

## Next Step

Wire a small operator-only trigger after the candidate import format is confirmed.

The trigger should call:

```text
BacktestCandidateImportService.LoadCandidates(...)
or BacktestCandidateImportService.LoadFromWatchlistCache(...)
BacktestDataStore.SaveCandidates(...)
BacktestDatasetBuilder.BuildDailyDataStoreAsync(...)
```

Keep it off the live Engine Start path.
