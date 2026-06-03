# 전략 계산 함수 사전

이 문서는 전략 계산 함수의 이름, 책임, 입력, 출력, 소유권을 고정하기 위한 기준서다.
전략이 늘어날수록 같은 단어가 다른 의미로 쓰일 수 있으므로, 함수 이름과 데이터 소유권을 먼저 정리한다.

문서 저장은 코드 변경이 아니다.
이 문서는 이후 구현, 백테스트, 실시간 엔진, Progress 표시가 같은 기준을 쓰기 위한 가이드다.

## 작성 원칙

- 함수명은 계산 대상과 소유권이 드러나야 한다.
- KRX 전일종가 기준가는 절대 전략 기준가나 마디 허리로 덮어쓰지 않는다.
- 백테스트와 실시간이 같은 판단을 해야 하는 계산은 공용 함수로 만든다.
- 전략 슬롯은 원본 REST/파일을 직접 보지 않고, 장부 또는 계산 서비스가 준 숫자만 사용한다.
- 문서에 없는 의미로 기존 필드를 재사용하지 않는다.

## 언어 변환 원칙

한글은 사람이 보는 전략 언어로 둔다.
영문은 프로그램이 실행하는 함수, 클래스, enum, JSON key 언어로 둔다.

이 원칙은 스타일 문제가 아니라 안전 문제다.
집/회사 PC, GitHub, WPF, JSON, PowerShell, 텔레그램, 키움 API가 섞이는 환경에서는 코드 내부 식별자를 영문으로 고정해야 인코딩, 검색, 리팩토링, 빌드 도구 충돌을 줄일 수 있다.

```text
한글 = 화면 표시 / 전략 설명 / 강의 메모
영문 = 코드 내부 함수명 / 상태 enum / 클래스명 / JSON key
매핑표 = 한글 전략 언어와 영문 실행 언어의 통역 사전
```

새 코드에는 한글 함수명, 한글 enum, 한글 property를 만들지 않는다.
화면과 로그에 한글이 필요하면 영문 상태값을 한글 표시 문자열로 변환한다.

### 기본 명명 규칙

```text
찾기/탐색: Find...
계산: Calculate...
측정: Measure...
판정: Evaluate...
감지: Detect...
분류: Classify...
해결/선택: Resolve...
작성/스냅샷: Build...
적용: Apply...
저장: Save...
```

예:

```text
강한 이탈       -> DetectStrongBaseBreak
기준가 후보     -> FindBasePriceCandidates
기준가 분류     -> ClassifyBasePrice
손절 위험 측정  -> MeasureStopRisk
마디 허리 계산  -> CalculateMadiWaistZone
눌림 상태 판정  -> ClassifyPullbackState
혹시 저점반전   -> DetectSuspicionLowReversal
혹시 고점소진   -> DetectSuspicionHighExhaustion
```

### 한글 표시명과 영문 상태명

| 한글 전략 언어 | 영문 내부명 | 용도 |
| --- | --- | --- |
| 기준가 | `StrategyBasePrice` | 전략 판단용 가격대 |
| KRX 전일종가 기준가 | `KrxPreviousCloseBasePrice` | 화면 색상/등락률 기준 |
| 일반 기준가 | `StrategyBasePriceType.General` | 지지/저항 후보 |
| 손절 가능 기준가 | `StrategyBasePriceType.StopCapable` | 손절선 후보 |
| 절대 기준가 | `StrategyBasePriceType.Absolute` | 이탈 시 시나리오 무효 |
| 기준봉 | `BaseCandle` | 특정 봉 기준 |
| 후보 게이트 기준봉 | `GateBaseCandle` | 목록 편입 필터 |
| 기준마디 | `MadiSegment` | 기준봉 포함 상승/하락 구간 |
| 허리 | `MadiWaistZone` | 마디 중심 지지/저항 영역 |
| 허리 지지 | `WaistSupport` | 허리 위 방어 |
| 허리 이탈 | `WaistBreak` | 허리 아래 마감/이탈 |
| 허리 회복 | `WaistRecovery` | 허리 재돌파/회복 |
| 눌림 | `Pullback` | 조정 구간 |
| 정상 눌림 | `PullbackState.Normal` | 거래 감소 + 구조 생존 |
| 위험 눌림 | `PullbackState.VolumeDanger` | 거래 증가 + 이탈 위험 |
| 돌파 | `Breakout` | 기준가/전고/20봉 고가 돌파 |
| 트랩 | `TrapCandidate` | 이탈/돌파 실패 후보 |
| 강한 이탈 | `BaseBreakState.StrongBreak` | 거래량 동반 기준가 이탈 |
| 첫 이탈 | `BaseBreakState.FirstBreak` | 첫 기준가 이탈 |
| 두 번째 이탈 | `BaseBreakState.SecondBreak` | 재이탈 |
| 반등 저항 | `BaseBreakState.ReboundRejected` | 회복 실패 |
| 혹시 저점반전 | `SuspicionLowReversal` | 저점 거래량 다이버전스 |
| 혹시 고점소진 | `SuspicionHighExhaustion` | 고점 거래량 다이버전스 |
| 거래량 증가 | `VolumeState.Expanding` | 거래량 이평 상향 |
| 거래량 폭증 | `VolumeState.Explosive` | 평균 대비 큰 거래량 |
| 거래량 동반 이탈 | `VolumeState.DangerBreakdown` | 기준가 이탈 + 거래량 증가 |
| 마지막 돌파 60선 | `LastBreakoutMa60Floor` | 바닥 최저가 대용 기준 |
| 60선 재지지 | `Ma60RetestSupport` | 5이평/가격이 60이평 위에서 버팀 |
| 작은 언덕 | `SmallHillStructure` | 5이평이 60이평으로 올라가며 만든 재상승 전 언덕 |
| 언덕 돌파 | `SmallHillBreakout` | 작은 언덕 고점 돌파 |

### 변환 함수 원칙

UI, 로그, 텔레그램에는 한글 표시명이 필요하다.
하지만 내부 판단은 영문 enum으로 유지한다.

함수 후보:

```text
ToKoreanLabel(BaseBreakState state)
ToKoreanLabel(PullbackState state)
ToKoreanLabel(VolumeState state)
ToKoreanLabel(StrategyBasePriceType type)
ToKoreanProgressText(StrategyProgressSnapshot snapshot)
```

주의:

한글 표시 문자열을 기준으로 전략을 판단하지 않는다.
전략 판단은 반드시 enum, bool, 숫자값으로 한다.
표시 문자열은 마지막 렌더링 단계에서만 사용한다.

## 용어 소유권

| 용어 | 의미 | 소유권 | 덮어쓰기 금지 |
| --- | --- | --- | --- |
| `KrxPreviousCloseBasePrice` | KRX 전일종가 기준가 | 화면 색상/등락률/호가 색상 기준 | 절대 덮어쓰기 금지 |
| `StrategyBasePrice` | 전략 판단용 기준가 | 전략 계산부 | KRX 기준가와 별도 |
| `GateBaseCandle` | 후보 편입용 일봉 기준봉 | 후보 게이트 | 전략 소마디와 별도 |
| `EntryMadiLow` | 3분/5분 소마디 생명선 저점 | 진입 마디 계산부 | 마디 폐기 기준 |
| `MadiWaistZone` | 기준마디 허리 가격대 | 마디 분석부 | 산술 50%와 다름 |
| `High20` | 최근 20봉 고가 | 전략분봉 장부 | 돌파 트리거 |
| `MA60` | 분봉 60이평 | 전략분봉 장부 | 이평선 회복 기준 |
| `RewardRiskRatio` | 기대수익/위험 비율 | 전략 필터 | 실주문 전 필수 확인 |

## 필수 함수 후보

### GetKrxPreviousCloseBasePrice

```text
역할: 화면과 전략 공통의 KRX 전일종가 기준가를 조회한다.
입력: 종목코드, 관심종목 캐시, 일봉 데이터
출력: KRX 전일종가
소유권: 기준가 잠금 담당
공용성: 백테스트/실시간 공용
주의: NXT 현재가, SOR 현재가, 전략 기준가가 이 값을 덮어쓰면 안 된다.
```

### ResolveActiveMarketForRealtime

```text
역할: 0s 장운영구분, 시간대, 종목의 NXT 가능 여부로 실시간 시장을 결정한다.
입력: 0s 상태, 현재시간, SupportsNxt, selectedStockMarket
출력: KRX / NXT / Unknown
소유권: 실시간 라우팅
공용성: 실시간 전용
주의: KRX fallback은 미확정 기본값이지 NXT 데이터 대체값이 아니다.
```

### FindGateBaseCandle

```text
역할: 최근 10거래일 안에서 후보 게이트 기준봉을 찾는다.
입력: 일봉 OHLCV, 거래대금, 등락률
출력: GateBaseCandle 정보, D+ offset
소유권: 후보 게이트
공용성: 백테스트/실시간 공용
주의: GateBaseCandle은 전략 소마디 기준봉과 다르다.
```

### CalculateMadiSegment

```text
역할: 기준봉이 포함된 상승마디의 시작/고가/저가/종료 후보를 계산한다.
입력: 상위봉 또는 분봉 OHLCV, 기준봉 후보
출력: MadiStartPrice, MadiHighPrice, MadiLowPrice, MadiEndTime
소유권: 마디 분석부
공용성: 백테스트/실시간 공용
주의: 기준봉 하나만 마디로 보지 않는다. 상승 시작점부터 조정 직전까지 본다.
```

### CalculateMadiWaistZone

```text
역할: 마디 안에서 실제 지지/저항이 모이는 허리 가격대를 계산한다.
입력: MadiSegment, 봉별 OHLCV, 전고점/전저점/종가/거래량
출력: MadiWaistLow, MadiWaistHigh, WaistScore
소유권: 마디 분석부
공용성: 백테스트/실시간 공용
주의: 허리는 산술 50%가 아니다. 가격 클러스터와 지지/저항 반복을 본다.
```

### CalculateMadiEfficiency

```text
역할: 거래대금 대비 가격 전진 효율을 계산한다.
입력: MadiRate, MadiTradingValue
출력: MadiEfficiency
소유권: 마디 품질 필터
공용성: 백테스트/실시간 공용
공식: 마디 효율 = 마디 상승률 / 마디 거래대금억
주의: 거래대금은 큰데 상승률이 낮으면 무거운 마디 또는 트랩 후보로 본다.
```

### FindEntryMadi

```text
역할: 3분/5분봉에서 실제 진입 후보가 되는 소마디를 찾는다.
입력: 분봉 OHLCV, 거래량/거래대금, High20, MA 정보
출력: EntryMadiStart, EntryMadiHigh, EntryMadiLow, EntryMadiWaist
소유권: 진입 마디 계산부
공용성: 백테스트/실시간 공용
주의: EntryMadiLow는 소마디 생명선이다. 이탈하면 해당 소마디는 폐기한다.
```

### EvaluateEntryMadiValidity

```text
역할: 소마디가 아직 살아 있는지 판단한다.
입력: EntryMadiLow, 현재가, 최근 봉 저가, 거래량
출력: Valid / Broken / TrapCandidate
소유권: 진입 마디 계산부
공용성: 백테스트/실시간 공용
주의: 한 번 이탈은 트랩일 수 있다. 거래량 동반 이탈과 두 번째 이탈은 강한 경고다.
```

### EvaluateMa60Recovery

```text
역할: 10분/15분 MA60 눌림과 회복 여부를 판단한다.
입력: StrategyMinuteFrameSnapshot, 현재가
출력: Ma60Ready, TouchedMa60, RecoveredMa60
소유권: 이평선 전략
공용성: 백테스트/실시간 공용
주의: MA60은 타이밍 장치다. 마디 허리와 동일한 기준가가 아니다.
```

### EvaluateHigh20Breakout

```text
역할: 3분/5분 최근 20봉 고가 돌파 여부를 판단한다.
입력: High20, 현재가, 봉 거래량
출력: BreakoutTriggered, BreakoutPrice
소유권: 돌파 트리거
공용성: 백테스트/실시간 공용
주의: 돌파만으로 매수하지 않는다. 손절선과 목표공간을 함께 본다.
```

### CalculateRewardRiskRatio

```text
역할: 진입 후보의 손익비를 계산한다.
입력: EntryPrice, StopPrice, TargetPrice
출력: RewardRiskRatio
소유권: 위험 필터
공용성: 백테스트/실시간 공용
주의: 손익비가 부족하면 신호가 좋아도 진입 금지다.
```

### StrategyExitFirstPlanner

```text
역할: 매수 신호가 주문 단계로 넘어가기 전에 탈출 계획을 먼저 검증한다.
입력: EntryPrice, StopPrice, TargetPrice, 최소 손익비, 손절폭 허용 범위
출력: StrategyExitFirstPlan
소유권: 위험 필터
공용성: 백테스트/실시간 공용
현재 구현: Services/Strategies/Core/StrategyExitFirstPlan.cs
연결 문서: Docs/Strategies/exit-first-risk-filter.md
주의: 이 함수는 주문을 보내지 않는다. NoBuyReasons가 하나라도 있으면 전략은 추적만 하고 주문 핸드오프를 막는다.
```

현재 1차 연결 슬롯:

```text
Slot 1: SOR 10m MA60 + 3m Breakout
Slot 2: SOR 15m MA60 + 5m Breakout
Slot 3: SOR 10m MA60 + 5m Breakout
Slot 5: Intraday 15m Base + 1m Trigger
```

Progress에는 `exit-first RR` 단계로 표시한다.
디버그 스냅샷에는 `EntryPrice`, `StopPrice`, `TargetPrice`, `RewardRiskRatio`, `NoBuyReasons`를 저장한다.

### EvaluateNoBuyZone

```text
역할: 사면 안 되는 자리인지 먼저 제거한다.
입력: 현재가, MadiWaistZone, EntryMadiLow, 거래량 이탈, 상위 저항
출력: NoBuyReason 목록
소유권: 위험 필터
공용성: 백테스트/실시간 공용
주의: 자동매매는 매수 버튼이 아니라 위험 필터다.
```

### BuildStrategyProgressSnapshot

```text
역할: 전략별 진행 단계를 0~100%로 변환한다.
입력: 전략 평가 결과, 매수 단계, 매도 단계
출력: StrategyProgressSnapshot
소유권: Progress 표시부
공용성: 실시간/Paper 공용
주의: 전략별 Progress는 공유하지 않는다. 각 전략은 자기 상태를 따로 가진다.
```

### SaveSelectedStrategyMinuteDebugSnapshot

```text
역할: 실행 중 메모리에 있는 선택 종목의 전략분봉 장부와 Progress 결과를 JSON으로 저장하고, 현재 화면 차트를 PNG로 함께 저장한다.
입력: 선택 종목, StrategyMinuteSnapshotSet, StrategyMinuteDataStatus, StrategyEvaluationResult
출력: Storage/DebugSnapshots/{timestamp}-{code}-{market}-strategy-minute.json, Storage/DebugSnapshots/{timestamp}-{code}-{market}-chart.png
소유권: 디버그/검증 통로
공용성: 실시간 검증 전용
주의: 선택 종목을 즉시 확인하기 위한 수동 저장이다. GitHub 백업 대상이 아니다. MD 리포트는 만들지 않고 화면 Progress, JSON, 차트 PNG만 사용한다. 전략 장부에 15분봉이 준비되어 있으면 저장용 PNG는 15분봉 120봉 문맥을 우선 사용하고, 없을 때만 현재 화면 차트로 대체한다.
```

### SaveAllStrategyMinuteDebugSnapshots

```text
역할: Engine Start ON 상태에서 후보/보유/최근조회에 걸린 종목의 전략분봉 장부와 Progress 결과를 한 JSON으로 묶고, 현재 화면 차트 PNG를 함께 저장한다.
입력: WatchStockItem 목록, StrategyMinuteSnapshotSet, StrategyMinuteDataStatus, StrategyEvaluationResult
출력: Storage/DebugSnapshots/{timestamp}-ALL-strategy-minute.json, Storage/DebugSnapshots/{timestamp}-ALL-chart.png
소유권: 디버그/검증 통로
공용성: 실시간 검증 전용
주의: 전략실 `Codex 확인` 스위치가 ON이고 Engine Start가 ON일 때만 장중 KRX/NXT 시간대에 3분마다 자동 저장한다. REST/뉴스/공시를 새로 호출하지 않고 메모리 장부와 차트 데이터만 읽는다. 저장용 PNG는 15분봉 120봉 문맥을 우선 사용하며, 없을 때만 현재 화면 차트로 대체한다. 자동 저장은 화면 종목을 바꾸지 않으며, PNG는 내가 기준마디/허리/기준가를 눈으로 검증하기 위한 자료다. GitHub 백업 대상이 아니다.
```

### SaveAiReviewRequest

```text
역할: Codex 확인 스냅샷을 AI 차트검토 큐 요청서로 배달한다.
입력: 저장된 strategy-minute JSON, chart PNG 경로, Progress, OrderIntent
출력: Storage/AiReviewQueue/pending/{timestamp}-chart-review-request.json
소유권: AI 검토 배달부
공용성: 수동 Codex 확인 / 향후 OpenAI API worker
주의: 이 함수는 AI를 직접 호출하지 않는다. 신호 배달부로서 요청서를 pending 큐에 놓기만 한다. 향후 API worker가 done/failed로 이동시키며 텔레그램 알림과 OrderIntent 반영을 담당한다. GitHub 백업 대상이 아니다.
```

## 전략 연결 원칙

초기 연결 순서는 다음과 같다.

```text
1. 계산값 로그
2. Progress 보조 표시
3. 백테스트
4. Paper / 텔레그램 알림
5. Live Orders 검토
```

마디 함수는 바로 실주문에 연결하지 않는다.
이평선 전략과 겹치는 구간을 먼저 검증하고, 마디 허리/손익비가 실제로 신호 품질을 높이는지 확인한다.

## 강의 용어 함수화

강의에서 쓰는 단어는 감각어로 남겨두지 않는다.
프로그램에서는 같은 단어를 상태값, 함수, Progress 단계로 바꾸어 사용한다.

이 장의 목적은 강의 내용을 그대로 매수 공식으로 만드는 것이 아니다.
강의 단어를 백테스트, 실시간 장부, Progress 표시, Codex 확인 스냅샷이 함께 읽을 수 있는 숫자로 번역하는 것이다.

### 프로세스 순서

전략 프로세스는 강의 목록 순서를 따른다.

```text
1. 손절 기준가를 먼저 찾는다.
2. 손익비가 안 나오면 진입하지 않는다.
3. 기준가와 기준마디가 살아 있는지 확인한다.
4. 이평선 위치와 시간대별 방향을 확인한다.
5. 거래량/거래대금이 다시 붙는지 확인한다.
6. 하위 시간대에서 실제 타격 지점을 좁힌다.
7. 매수 후에는 같은 Progress 안에서 매도 추적을 이어간다.
```

70% 이전은 매수 후보를 만드는 구간이다.
70%는 매수 완료 또는 매수 가능 구간이다.
70~100%는 보유 후 손절/목표/트레일링을 추적하는 구간이다.

### 강한 이탈

`강한 이탈`은 단순히 기준가 아래로 내려간 상태가 아니다.
강의 내용에서는 다음 개념들이 합쳐진 상태로 본다.

```text
중요 기준가 이탈
+ 거래량 동반
+ 두 번째 이탈 또는 회복 실패
+ 반등 시 같은 기준가에서 저항
```

프로그램 상태명:

```text
BaseBreakState.None
BaseBreakState.FirstBreak
BaseBreakState.TrapCandidate
BaseBreakState.Recovered
BaseBreakState.SecondBreak
BaseBreakState.StrongBreak
BaseBreakState.ReboundRejected
```

함수 후보:

```text
DetectBaseBreakState
DetectStrongBaseBreak
DetectBaseRecovery
DetectReboundRejectionAtBase
```

입력값:

```text
StrategyBasePrice
CurrentPrice
CurrentCandleClose
CurrentCandleLow
BreakCount
RecoveryCount
Volume
VolumeMA5
VolumeMA20
VolumeMA60
TradeValue
PreviousSupportCount
PreviousResistanceCount
```

1차 수치화 기준:

```text
FirstBreak:
  종가가 기준가 아래에서 마감

TrapCandidate:
  기준가를 이탈했지만 거래량이 크지 않고 다음 봉에서 회복 가능성이 남아 있음

Recovered:
  이탈 후 기준가 위로 다시 종가 회복

SecondBreak:
  기준가를 한 번 회복했거나 지지받은 뒤 다시 이탈

StrongBreak:
  기준가 이탈 + 거래량이 VolumeMA20 대비 1.8배 이상
  또는 기준가 이탈 + 거래대금이 최근 평균 대비 1.8배 이상
  또는 두 번째 이탈 + 종가 기준 미회복

ReboundRejected:
  기준가 아래로 내려간 뒤 반등했지만 기준가 부근에서 다시 막힘
```

주의:

첫 이탈은 트랩일 수 있다.
거래량이 작고 바로 회복하면 강한 이탈로 보지 않는다.
거래량을 동반한 두 번째 이탈이나 기준가 회복 실패는 강한 경고로 본다.

### 기준가

`기준가`는 KRX 전일종가 기준가와 다르다.
KRX 전일종가 기준가는 화면 색상과 등락률 기준이며 절대 덮어쓰지 않는다.
전략 기준가는 매수/손절/익절 판단용 가격대다.

손절 강의 확인 결과, 기준가는 아래처럼 분리한다.

```text
일반 기준가:
  지지/저항 후보지만 손절선으로는 애매한 가격대

손절 가능 기준가:
  이탈 시 해당 진입 시나리오를 의심해야 하는 가격대

절대 기준가:
  손절선으로 적합한 기준가
  이탈 시 진입 논리가 무효화되는 가격대
```

같은 종목, 같은 진입 자리라도 손절 기준가는 달라질 수 있다.
진입 시점의 상위 마디, 기준가 위치, 마디 허리 지지 여부, 거래량 상태가 다르면 `StopBasePrice`도 다르게 선택한다.
따라서 손절가는 단일 고정 퍼센트가 아니라 `ResolveStopBasePrice`가 선택한 가격대를 우선한다.

프로그램 상태명:

```text
StrategyBasePriceType.General
StrategyBasePriceType.StopCapable
StrategyBasePriceType.Absolute
StrategyBasePriceType.TargetResistance
```

함수 후보:

```text
FindBasePriceCandidates
ClassifyBasePrice
ResolveStopBasePrice
MeasureBasePriceStrength
MeasureBasePriceTouches
```

핵심 판단:

```text
일반 기준가:
  지지/저항 후보이지만 손절선으로 쓰기에는 아직 약함

손절 가능 기준가:
  이탈 시 매매 시나리오가 깨졌다고 볼 수 있는 가격대

절대 기준가:
  손절 기준으로 적합하고, 이탈 시 진입 논리가 무효화되는 가격대

목표 저항 기준가:
  위쪽에 있는 익절/저항 후보 가격대
```

진입 전 필수:

```text
EntryPrice
StopBasePrice
TargetBasePrice
StopRiskPercent
TargetProfitPercent
RewardRiskRatio
```

손절 기준가가 없으면 진입하지 않는다.

### 반등 시 손절

`반등 시 손절`은 아무 하락 후 반등에서 쓰는 규칙이 아니다.
먼저 강한 기준마디가 있어야 하며, 그 기준마디가 깨진 뒤 반등이 나올 때 탈출 또는 손절을 판단한다.

프로그램 상태명:

```text
ReboundStopState.None
ReboundStopState.WaitingForRebound
ReboundStopState.ReboundNearBase
ReboundStopState.ReboundRejected
ReboundStopState.ExitConfirmed
```

함수 후보:

```text
DetectStrongMadiSegment
DetectBrokenMadiForReboundStop
DetectReboundToStopBasePrice
DetectReboundRejectionForExit
```

입력값:

```text
StrongMadiSegment
StopBasePrice
AbsoluteStopBasePrice
CurrentPrice
CurrentCandleClose
ReboundHigh
Volume
VolumeMA20
```

1차 수치화 기준:

```text
WaitingForRebound:
  강한 기준마디 확인 후 기준가 또는 허리 이탈

ReboundNearBase:
  이탈 후 현재가가 StopBasePrice 근처까지 반등

ReboundRejected:
  반등했지만 StopBasePrice 또는 허리 부근에서 종가 회복 실패

ExitConfirmed:
  ReboundRejected + 거래량 증가
  또는 AbsoluteStopBasePrice 미회복
```

주의:

반등 시 손절은 손실을 줄이는 탈출 시나리오다.
새 매수 신호가 아니다.

### 기준마디와 허리

`기준마디`는 기준봉 하나가 아니라 기준봉이 포함된 상승 구간 전체다.
상승 시작점, 기준봉, 추가 상승, 조정 직전 고점까지를 하나의 마디 후보로 본다.

프로그램 상태명:

```text
MadiState.None
MadiState.Building
MadiState.Confirmed
MadiState.WaistTesting
MadiState.Alive
MadiState.Broken
MadiState.TrapRecovered
```

함수 후보:

```text
FindImpulseLeg
FindMadiSegment
FindMadiWaistZone
DetectWaistSupport
DetectWaistBreak
DetectWaistTrapRecovery
MeasureMadiRiskReward
```

입력값:

```text
MadiStartTime
MadiStartPrice
MadiHighPrice
MadiLowPrice
MadiEndTime
MadiVolume
MadiTradeValue
MadiWaistLow
MadiWaistHigh
WaistSupportCount
WaistBreakCount
```

허리 수치화 기준:

```text
1차 후보:
  마디 상승폭의 45~55% 구간

보정 후보:
  전고점/전저점/종가가 반복적으로 모이는 가격대
  조정 중 실제 지지 또는 저항이 발생한 가격대
  거래량이 줄어든 상태에서 방어된 가격대

강한 허리:
  중심 부근 가격 클러스터 + 2회 이상 지지/저항 확인
```

주의:

허리는 산술 50% 하나가 아니다.
처음에는 50%를 후보로 두고, 이후 지지/저항 반복과 거래량으로 보정한다.

### 눌림

`눌림`은 가격이 내려온 사실이 아니라 거래가 줄며 구조가 살아 있는 조정이다.

프로그램 상태명:

```text
PullbackState.None
PullbackState.Normal
PullbackState.DeepButAlive
PullbackState.VolumeDanger
PullbackState.Broken
```

함수 후보:

```text
DetectPullbackRange
DetectPullbackVolumeDryUp
DetectPullbackSupport
ClassifyPullbackState
```

정상 눌림 후보:

```text
마디 허리 위 또는 주요 기준가 위
거래량이 VolumeMA20 아래로 감소
음봉이지만 거래대금이 급증하지 않음
하위 시간대에서 재상승 준비
```

위험 눌림:

```text
기준가 이탈
거래량 증가
저점 갱신 반복
반등 시 기준가 저항
```

### 거래량 이평선

`거래가 늘었다`는 말은 거래량 이평선으로 수치화한다.
가격 돌파보다 먼저 또는 동시에 거래량 5이평이 20/60이평을 넘어서는지 확인한다.

프로그램 상태명:

```text
VolumeState.Dry
VolumeState.Average
VolumeState.Expanding
VolumeState.Explosive
VolumeState.DangerBreakdown
```

함수 후보:

```text
CalculateVolumeMA
MeasureVolumeExpansion
DetectVolumeMaCross
DetectPullbackVolumeDryUp
DetectHeavyVolumeBreakdown
```

입력값:

```text
Volume
VolumeMA5
VolumeMA20
VolumeMA60
TradeValue
TradeValueMA20
CurrentVolumeToMA20Ratio
CurrentVolumeToMA60Ratio
```

1차 기준:

```text
VolumeState.Dry:
  VolumeMA5 < VolumeMA20 and 현재 봉 거래량도 평균 이하

VolumeState.Expanding:
  VolumeMA5 > VolumeMA20 and 현재 봉이 양봉

VolumeState.Explosive:
  현재 봉 거래량 >= VolumeMA20 * 2.0

VolumeState.DangerBreakdown:
  기준가 이탈 + 현재 봉 거래량 >= VolumeMA20 * 1.8
```

주의:

거래량 증가는 단독 매수 신호가 아니다.
가격 위치, 기준가, 마디 허리, 손익비와 같이 본다.

### 60선 재지지 언덕 돌파

바닥 최저가를 정확히 찾기는 어렵다.
따라서 마지막으로 돌파한 60이평을 바닥 대용 기준으로 삼는다.

핵심 흐름:

```text
1차 또는 2차 상승
→ 첫 60선 이탈
→ 하락세 둔화
→ 중심 근처 지지
→ 작은 언덕 형성
→ 2차 매수세 유입
→ 5이평이 60이평으로 올라감
→ 5이평이 60이평 위에서 지지받는지 확인
→ 거래가 줄며 더 이상 하락하지 않음
→ 다시 매수세 유입
→ 앞의 5이평/60이평 언덕 고점 돌파 확인
```

이 시나리오는 바닥을 맞히는 매매가 아니다.
바닥에서 산 사람이 파는 구간을 지나, 5이평이 60이평 위에서 다시 지지받는지를 확인하는 매매다.

프로그램 상태명:

```text
Ma60RetestState.None
Ma60RetestState.LastBreakoutFound
Ma60RetestState.FirstMa60Break
Ma60RetestState.DeclineSlowing
Ma60RetestState.CenterSupportSuspected
Ma60RetestState.SmallHillBuilding
Ma60RetestState.Ma5ClimbingToMa60
Ma60RetestState.Ma60SupportConfirmed
Ma60RetestState.SmallHillBreakout
Ma60RetestState.Failed
```

함수 후보:

```text
FindLastBreakoutMa60Floor
DetectFirstMa60BreakAfterRise
DetectDeclineSlowingNearCenter
DetectSmallHillStructure
DetectMa5ClimbingToMa60
DetectMa60RetestSupport
DetectSmallHillBreakout
```

입력값:

```text
Close
Open
High
Low
Volume
TradeValue
MA5
MA20
MA60
VolumeMA5
VolumeMA20
VolumeMA60
LastBreakoutMa60Price
SmallHillHigh
SmallHillLow
SmallHillVolume
```

1차 수치화 기준:

```text
LastBreakoutFound:
  가격이 MA60 아래에서 위로 올라선 마지막 구간을 기록

FirstMa60Break:
  상승 후 처음으로 종가가 MA60 아래로 이탈

DeclineSlowing:
  저점 갱신 폭이 줄고 거래량/거래대금이 감소

CenterSupportSuspected:
  마지막 돌파 MA60 또는 마디 허리 근처에서 저가 방어

SmallHillBuilding:
  MA5가 MA60 쪽으로 올라가며 작은 고점/저점 구조 형성

Ma60SupportConfirmed:
  MA5 또는 가격이 MA60 근처에서 2봉 이상 버팀
  거래량은 과열이 아니라 줄어드는 상태

SmallHillBreakout:
  작은 언덕 고점 돌파
  동시에 거래량/거래대금 재증가
```

손절 기준:

```text
StopPrice = SmallHillLow
또는 LastBreakoutMa60Price 아래 종가 이탈
```

진입 후보:

```text
Ma60SupportConfirmed
+ SmallHillBreakout
+ VolumeState.Expanding
+ NoBuyReasons.Count == 0
```

주의:

이 시나리오는 60이평 자체를 맹신하지 않는다.
60선은 바닥 대용 기준이고, 실제 진입은 작은 언덕 돌파와 거래량 재유입을 확인한 뒤에만 본다.

### 시간대 분해

우리 전략은 큰 시간대에서 작은 시간대로 내려오며 수익 구간을 좁힌다.

```text
일봉:
  이 종목을 볼 이유가 있는가
  기준봉, 거래대금, 장기 기준가

15분 / 10분:
  마디 안에서 방향이 살아 있는가
  15분 MA60 위 또는 회복 확인
  허리 위인지, 눌림인가, 다시 꺾이는가

5분 / 3분:
  사상대가 준비되는가
  거래량이 줄었다가 다시 붙는가
  전고/기준가 회복이 있는가

1분:
  실제 타격 지점
  손절을 짧게 둘 수 있는가
  체결/호가/거래량이 붙는가
```

15분 MA60은 현재 전략의 구조 판단 하단선이다.
15분 MA60 아래에서는 하위 시간대 신호가 좋아도 매수 신호가 아니라 회복 확인 구간으로 본다.

### Progress 연결

강의 용어는 Progress에 다음처럼 연결한다.

```text
0~20%:
  후보 게이트, 일봉 기준봉, 거래대금

20~40%:
  기준마디, 전략 기준가, 허리 후보

40~55%:
  15분/10분 방향, MA60 위치, 허리 생존

55~70%:
  5분/3분 눌림, 거래량 재점화, 돌파/회복

70%:
  매수 가능 또는 매수 완료

70~100%:
  손절 기준가, 목표가, 트레일링, 강한 이탈 감시
```

전략별 Progress는 공유하지 않는다.
같은 종목이라도 슬롯 1, 슬롯 2, 슬롯 3은 각자의 기준마디와 기준가를 별도로 가진다.

### 우선 구현 순서

처음부터 모든 단어를 구현하지 않는다.
다음 순서로 함수화한다.

```text
1. DetectBaseBreakState
2. DetectStrongBaseBreak
3. CalculateVolumeMA / MeasureVolumeExpansion
4. FindMadiSegment
5. FindMadiWaistZone
6. ClassifyPullbackState
7. BuildStrategyProgressSnapshot에 상태 반영
```

이 순서의 이유:

매수 신호보다 먼저 손절과 위험 이탈을 알아야 한다.
그 다음 거래량이 실제로 붙었는지 확인하고,
마지막에 기준마디와 허리를 통해 매수 가능 위치를 좁힌다.

## 구현 계약 v1

이 장은 실제 코드로 옮길 때의 입력/출력 계약이다.
함수명은 영문으로 고정하고, 화면/로그/문서 표시만 한글로 변환한다.

### CandleSeriesContext

```text
역할: 특정 종목/시장/분봉의 봉 묶음을 계산 함수에 전달한다.
입력: Code, Market, Minute, Bars, Now
출력: 계산 함수 공통 입력 컨텍스트
소유권: 전략분봉 장부 / 백테스트 장부
주의: 함수는 REST나 파일을 직접 보지 않는다. 이미 준비된 Bars만 본다.
```

필수 포함값:

```text
Code
Market
Minute
Bars
LastCompletedBar
CurrentBar
Now
```

### VolumeMovingAverageSet

```text
역할: 거래량/거래대금이 늘었는지 줄었는지 판단할 공통 숫자 묶음이다.
입력: Bars
출력: VolumeMA5, VolumeMA20, VolumeMA60, TradeValueMA5, TradeValueMA20, TradeValueMA60
소유권: 거래량 계산부
공용성: 백테스트/실시간 공용
주의: "거래가 늘었다"는 말은 이 값으로만 판단한다.
```

1차 함수:

```text
CalculateVolumeMovingAverages(CandleSeriesContext context) -> VolumeMovingAverageSet
MeasureVolumeExpansion(CurrentBar, VolumeMovingAverageSet ma) -> VolumeExpansionResult
DetectVolumeMaCross(VolumeMovingAverageSet ma, CurrentBar) -> VolumeMaCrossState
```

### BasePriceCandidate

```text
역할: 지지/저항/손절/목표가 후보가 되는 가격대를 표현한다.
입력: 봉의 고가/저가/종가, 반복 지지/저항, 거래량
출력: Price, Type, Strength, TouchCount, Source
소유권: 기준가 계산부
주의: KRX 전일종가 기준가와 다른 값이다.
```

1차 함수:

```text
FindBasePriceCandidates(CandleSeriesContext context) -> IReadOnlyList<BasePriceCandidate>
ClassifyBasePrice(BasePriceCandidate candidate, CandleSeriesContext context) -> StrategyBasePriceType
ResolveStopBasePrice(EntryScenario scenario, IReadOnlyList<BasePriceCandidate> candidates) -> BasePriceCandidate?
ResolveTargetBasePrice(EntryScenario scenario, IReadOnlyList<BasePriceCandidate> candidates) -> BasePriceCandidate?
```

### MadiSegmentCandidate

```text
역할: 기준봉 하나가 아니라 기준봉이 포함된 상승/하락 구간 전체를 표현한다.
입력: 봉 묶음, 거래량/거래대금, 시작점/고점/조정 시작 후보
출력: StartTime, EndTime, StartPrice, HighPrice, LowPrice, Direction, TradeValue
소유권: 마디 계산부
주의: GateBaseCandle과 별도다. 전략마다 자기 마디를 따로 가진다.
```

1차 함수:

```text
FindImpulseLeg(CandleSeriesContext context) -> IReadOnlyList<MadiSegmentCandidate>
FindMadiSegment(CandleSeriesContext context, BaseCandle baseCandle) -> MadiSegmentCandidate?
MeasureMadiEfficiency(MadiSegmentCandidate madi) -> double
```

### MadiWaistZoneCandidate

```text
역할: 마디 중심 부근에서 실제 힘이 모인 지지/저항 영역을 표현한다.
입력: MadiSegmentCandidate, 봉 묶음, 전고/전저/종가 클러스터, 거래량 감소
출력: LowPrice, HighPrice, CenterPrice, Score, SupportCount, BreakCount
소유권: 허리 계산부
주의: 허리는 산술 50% 하나가 아니다. 50%는 후보일 뿐이다.
```

1차 함수:

```text
FindMadiWaistZone(MadiSegmentCandidate madi, CandleSeriesContext context) -> MadiWaistZoneCandidate?
DetectWaistSupport(MadiWaistZoneCandidate waist, CandleSeriesContext context) -> WaistSupportState
DetectWaistBreak(MadiWaistZoneCandidate waist, CandleSeriesContext context) -> BaseBreakState
DetectWaistTrapRecovery(MadiWaistZoneCandidate waist, CandleSeriesContext context) -> bool
```

### NoBuyReason

```text
역할: 사면 안 되는 이유를 먼저 제거한다.
입력: 현재가, 손절 기준가, 목표 기준가, 허리, 거래량 상태, 상위 저항
출력: NoBuyReason 목록
소유권: 위험 필터
주의: 이 목록이 비어야만 매수 후보가 될 수 있다.
```

1차 함수:

```text
EvaluateNoBuyZone(EntryScenario scenario) -> IReadOnlyList<NoBuyReason>
MeasureStopRisk(EntryPrice, StopPrice) -> double
MeasureTargetRoom(EntryPrice, TargetPrice) -> double
CalculateRewardRiskRatio(EntryPrice, StopPrice, TargetPrice) -> double
```

대표 NoBuyReason:

```text
NoStopBasePrice
RewardRiskTooLow
TargetRoomTooSmall
WaistBroken
StrongBaseBreak
VolumeDangerBreakdown
UpperResistanceTooNear
OneMinuteTriggerTooEarly
OrderBookSupportWeak
MarketBuyFlowWeak
```

### EntryScenario

```text
역할: 한 전략이 "지금 살 수 있는가"를 판단하기 위한 전체 묶음이다.
입력: 상위시간대 방향, 기준가, 마디, 허리, 거래량, 0B, 0D
출력: Progress, OrderIntent, NoBuyReasons
소유권: 전략 슬롯
주의: 같은 종목이라도 전략마다 EntryScenario는 따로 가진다.
```

포함값:

```text
Code
Market
SlotId
HigherTimeframeState
MadiSegment
WaistZone
StopBasePrice
TargetBasePrice
VolumeState
RealtimeFlow
OrderBookProbe
NoBuyReasons
RewardRiskRatio
```

### PredictableTradeScore

```text
역할: "안 살 이유가 없는가"를 숫자로 요약한다.
입력: EntryScenario
출력: RiskFilterScore, StructureScore, FlowScore, RewardRiskScore, TotalScore
소유권: 검증/Progress 표시부
주의: 점수는 설명용이다. 실주문은 반드시 NoBuyReason, RiskGuard, Live Orders를 통과해야 한다.
```

1차 배점:

```text
RiskFilterScore:
  손절 기준가 있음
  손절폭 작음
  목표 공간 있음
  손익비 1.5~2.0 이상

StructureScore:
  기준마디 있음
  허리 위 지지
  15분/10분 MA60 생존
  소마디 저점 미이탈

FlowScore:
  거래량 MA5 > MA60
  거래대금 재증가
  0B 시장가 매수 우위
  0D 매수잔량 지지

RewardRiskScore:
  StopRiskPercent 낮음
  TargetProfitPercent 충분
  윗저항까지 공간 있음
```

### 시간대별 함수 연결

```text
Daily:
  FindGateBaseCandle
  FindBasePriceCandidates

15m / 10m:
  FindMadiSegment
  FindMadiWaistZone
  EvaluateMa60Recovery
  ClassifyPullbackState

5m / 3m:
  FindEntryMadi
  EvaluateHigh20Breakout
  DetectPullbackVolumeDryUp
  MeasureVolumeExpansion

1m:
  DetectOneMinutePrecisionTrigger
  DetectVolumeMaCross
  DetectMarketBuyFlow
  DetectOrderBookSupport
```

### 1분 정밀 타격 함수 후보

1분봉은 초반부터 보지 않는다.
상위 시간대가 65~70% 부근까지 진행된 뒤 마지막 확인용으로만 쓴다.

```text
DetectOneMinutePrecisionTrigger
입력:
  1m CurrentBar
  1m MA5/MA20/MA60
  1m VolumeMA5/VolumeMA60
  RealtimeFlow 0B
  OrderBookProbe 0D

출력:
  OneMinuteTriggerState
  TriggerPrice
  StopAnchor
  Reason
```

1차 조건:

```text
가격:
  현재 1분봉 양봉
  현재가가 1분 MA60 위 또는 회복
  1분 MA5가 MA60을 회복 또는 위에서 지지

거래량:
  1분 거래량 MA5 > 거래량 MA60
  현재 봉 거래량이 직전 5봉 평균 이상

체결:
  최근 60초 시장가 매수 체결량 > 시장가 매도 체결량
  최근 60초 매수 체결 비율 55% 이상

호가:
  0D fresh
  매수잔량 비율 45% 이상
  현재가가 최우선 매수호가 아래로 무너지지 않음
```

주의:

1분 신호는 예측이 아니라 최종 확인이다.
상위 구조가 없으면 1분봉이 좋아도 매수하지 않는다.

## 금지 규칙

- `KrxPreviousCloseBasePrice`를 NXT 현재가나 SOR 현재가로 덮어쓰지 않는다.
- `GateBaseCandle`을 3분/5분 소마디 기준봉으로 재사용하지 않는다.
- 백테스트에서 쓴 계산식과 실시간에서 쓴 계산식이 다르면 안 된다.
- 전략 슬롯이 파일/REST를 직접 호출해 판단하지 않는다.
- 소마디 저점이 깨진 후보를 같은 소마디로 계속 추적하지 않는다.
