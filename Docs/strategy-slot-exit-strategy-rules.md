# Strategy Slot Exit Strategy Rules

## Purpose

Each buy strategy slot owns the quantity it bought. When duplicate buys are enabled, the same stock can have multiple slot positions, but each sell decision must only use that slot position quantity.

## Rules

- Buy slots can choose an exit strategy before the slot is turned on.
- Once a slot is on, its exit strategy selector is locked.
- Live and paper trading share the same decision labels, but execution remains separated.
- Manual Buy Stop Assist is not a buy strategy. It only handles manual or untagged holdings.
- Position ledger entries store both the entry strategy and the selected exit strategy.
- Order journal entries store the selected exit strategy for audit.

## Initial Exit Strategies

- `BASE_LOW_PROFIT_SCALE`: 기준봉 저가 + 2/4% 분할.
- `QUICK_REACTION_REENTRY`: 5분 안에 안 가면 정리하고 재진입 후보로 다시 대기.
- `PROFIT_SCALE_TRAIL`: 2/4% 익절 후 잔량 추적.
- `SWING_HOLD`: 며칠 보유 실험용 청산 틀.
- `MANUAL_BUY_STOP_ASSIST`: 수동매수 자동손절기. 수동/무태그 보유분 전용.

## Storage

Runtime slot configuration is saved under:

```text
Storage/StrategySlots/slot_config.json
```

This is local runtime state and is not backed up to GitHub.
