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
Backtest.MarketMode = MARKET_SPLIT
```

Until the operator-only condition loader is wired, use the existing TradingDashboard watchlist/search-condition cache.

Use:

```text
BacktestDailyDataStoreJob.RunAsync(...)
```

This loads condition 23 candidates, saves the candidate snapshot, downloads or reuses daily bars, and re-verifies base candles.

`MARKET_SPLIT` is the default backtest data mode. It keeps every KRX candidate as KRX and adds an extra NXT candidate for stocks whose stock-master row says `SupportsNxt = true`. Daily and minute files are therefore separated by `Code + Market`, not by code alone.

`KRX_ONLY` remains available for old KRX-only tests. `SOR_MIXED` remains a special experiment mode that can mirror KRX base-candle events into NXT execution rows; do not treat that as real NXT daily-candle verification.

Operator command:

```powershell
dotnet run -- --backtest-daily-datastore
```

The command does not open the dashboard window. It writes a summary to:

```text
Storage/Backtests/DataStore/metadata/last_daily_update_summary.json
Storage/Backtests/DataStore/metadata/daily_update_summary_{RunId}.json
```

Fallback:

```text
Config/watchlist_stock_cache.json
BacktestCandidateImportService.LoadFromWatchlistCache(...)
```

This avoids manually preparing stock lists if the condition loader is unavailable.

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

The daily operator trigger is:

```powershell
dotnet run -- --backtest-daily-datastore
```

The minute operator trigger is:

```powershell
dotnet run -- --backtest-minute-datastore
```

The minute trigger reads:

```text
Storage/Backtests/DataStore/base_candles/verified_base_candles.json
```

Then it downloads only configured intervals for stock/market pairs that have verified base candles:

```text
Backtest.MinuteIntervals = 1, 3, 5, 10, 15, 30
Backtest.MinuteFetchCount = 1200
```

Minute bars are upserted into:

```text
Storage/Backtests/DataStore/minute/{minute}m/{Code}_{Market}_{minute}m.json
Storage/Backtests/DataStore/metadata/last_minute_update_summary.json
Storage/Backtests/DataStore/metadata/minute_update_summary_{RunId}.json
```

Keep it off the live Engine Start path.

## Run Results

Strategy execution results must be stored separately from DataStore source data.

Use:

```text
BacktestRunStore.SaveRun(...)
```

Output:

```text
Storage/Backtests/Runs/{RunId}/signals.csv
Storage/Backtests/Runs/{RunId}/trades.csv
Storage/Backtests/Runs/{RunId}/summaries.json
Storage/Backtests/Runs/{RunId}/strategy_comparison.csv
```

Do not write strategy outputs back into `DataStore`.

## First Strategy Smoke Test

Operator command:

```powershell
dotnet run -- --backtest-ten-pullback-five-breakout
dotnet run -- --backtest-ten-pullback-five-breakout-3h
```

Strategy:

```text
TEN_MA60_PULLBACK_FIVE_HIGH20_BREAK
```

Rules:

```text
1. Use only verified base-candle stock/market pairs.
2. Start after the earliest verified base-candle date for that stock/market pair.
3. 10-minute bars must have crossed down from MA60 and still be below MA60.
4. Current 5-minute bar must be bullish.
5. Current 5-minute close must break the previous 20 completed 5-minute bars' highest close.
6. Current 5-minute open must be inside the previous high-close candle's range.
7. Current 5-minute volume must exceed the volume of the previous high-close bar.
8. If the requested same-day holding window is not available after entry, skip the signal.
```

Observation exit:

```text
Hold for the requested same-day 5-minute bar count.
30-minute smoke test: 6 bars.
3-hour smoke test: 36 bars.
Record MaxHigh, MinLow, MFE, MAE, and the holding-window exit close.
```

This is a signal-quality test, not a live strategy.

## Small Base Center Pullback Test

First-priority smoke test:

```powershell
dotnet run -- --backtest-small-base-center-10m-3m
dotnet run -- --backtest-small-base-center-10m-3m-1m
dotnet run -- --backtest-small-base-center-15m-5m-1m
```

Strategy:

```text
SMALL_BASE_MA60_CENTER_PULLBACK
```

Rules:

```text
Base candle: completed 10-minute bar
Support candle: completed 3-minute bar
Optional trigger candle: completed 1-minute bar
Observation window: same-day 180 minutes
Stop reference: small base candle low

Previous 10-minute close < MA60
Small base low < MA60
Small base close > MA60
Small base close > open
Small base rise >= 1.0%
Small base trading value >= 1,000,000,000 KRW

3-minute entry close between small base center and small base close
3-minute volume >= previous 20-bar average volume * 1.2

1-minute trigger variant:
1-minute trigger close > open
1-minute trigger close > previous 1-minute high
1-minute trigger volume >= previous 20-bar average volume * 1.2
```

Version roles:

```text
10m / 3m / 1m = aggressive
15m / 5m / 1m = stable
```

Quality metrics:

```text
RiskRate = (EntryPrice - SmallBaseLow) / EntryPrice
MaxR = (MaxHigh - EntryPrice) / (EntryPrice - SmallBaseLow)
MinR = (MinLow - EntryPrice) / (EntryPrice - SmallBaseLow)
```
