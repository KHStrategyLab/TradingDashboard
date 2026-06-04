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

## Exit Principle Study 2026-06-04

매도 원칙은 고정 수익률이나 고정 손실금액으로 먼저 정하지 않는다.

```text
10% 수익이면 무조건 익절
-3%면 무조건 손절
100만원 벌면 종료
100만원 잃으면 종료
```

이런 숫자만의 원칙은 차트 구조를 보지 못할 때 쓰는 최후의 안전장치다.
기술적 매도 원칙은 먼저 차트의 기준가, 파동, 트랩, 거래량으로 정한다.

### 1. 기준가 익절/손절

- 익절은 위쪽 확실한 기준가에서 한다.
- 손절은 아래쪽 확실한 기준가가 깨질 때 한다.
- 기준가는 거래량이 터진 캔들, 지지/저항 반복, 매물대, 돌파 시작점에서 찾는다.
- 손절 기준가는 익절 기준가보다 더 타이트하게 잡는다.
- 기준가가 깨졌다면 -5%를 기다리지 않는다. 구조가 깨진 것이 먼저다.

### 2. N-shape / Box-shape Target

확실한 위쪽 기준가가 없으면 파동 목표를 사용한다.

```text
N-shape target = 1차 상승폭을 다음 상승 구간에 투영
Box-shape target = 박스 높이를 돌파 지점 위로 투영
```

기준가, N-shape, Box-shape 목표가 겹칠수록 익절 후보 신뢰도가 높다.

### 3. One-way / Trap Hold

원웨이 흐름이나 트랩 후 회복 패턴이 나오면 최초 목표보다 길게 볼 수 있다.

- 거래량이 계속 들어온다.
- 조정이 얕다.
- 허리를 살짝 건드린 뒤 다시 올라간다.
- 기준가를 이탈한 척하고 회복한다.

이 경우 전량 익절을 늦출 수 있지만, 본전 또는 구조 기준가에는 반드시 방어선을 둔다.

### 4. Volume Expansion Exit

거래량 증가는 익절 신호가 될 수 있다.
단, 거래량 증가만 단독으로 보지 않는다.

좋은 익절 조건:

```text
위쪽 기준가 도달
+ N-shape / Box-shape 목표 근접
+ 거래량 급증
+ 윗꼬리 또는 상승각 둔화
```

역배열 반등, 큰 매물대, 전고 저항 근처에서 거래량이 터지면 단기 고점 가능성이 커진다.
반대로 원웨이 강세장에서 거래량이 터지는 것은 추세 지속일 수 있으므로 구조와 함께 판단한다.

### 5. Psychological Stop / Take-profit

고정 퍼센트 매도는 기본 원칙이 아니라 심리 붕괴 방지 장치다.

사용할 때:

- 기준가가 애매하다.
- 순간 급락으로 기술적 손절을 놓쳤다.
- 큰 손실 후 욕심/공포가 커졌다.
- 근거 없이 홀딩하고 싶어진다.

이 경우 사전에 정한 마지노선 손절/익절을 실행한다.

## Program Translation

프로그램은 매도 판단을 다음 순서로 계산한다.

```text
1. 구조 손절가가 있는가
2. 현재가가 구조 손절가를 강하게 이탈했는가
3. 위쪽 기준가 또는 N/Box 목표에 도달했는가
4. 목표 근처에서 거래량 증가/윗꼬리/시장가 매도 증가가 나오는가
5. 원웨이 또는 트랩 회복이면 잔량 홀딩이 가능한가
6. 기준가가 애매하면 비상 고정 손절/익절 규칙을 적용할 것인가
```

이 원칙은 매수 전략보다 우선한다.
매수는 가능성이고, 매도는 생존이다.

## Storage

Runtime slot configuration is saved under:

```text
Storage/StrategySlots/slot_config.json
```

This is local runtime state and is not backed up to GitHub.
