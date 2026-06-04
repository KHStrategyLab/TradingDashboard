# Hybrid Exit Backtest Plan

이 문서는 실전 매도 주문을 바로 연결하기 위한 문서가 아니다.
매도 공식을 백테스트하고, 서로 비교하고, 나중에 하이브리드 매도 판정기로 합치기 위한 준비 문서다.

현재 원칙:

```text
매수는 가능성이다.
매도는 생존이다.

수익은 나눠서 보호한다.
손절은 구조가 깨지면 전량이다.
```

## 목표

매도 공식은 하나가 아니다.
전략별, 시간대별, 보유 상태별로 여러 매도 신호가 동시에 나올 수 있다.

따라서 최종 구조는 다음처럼 간다.

```text
ExitSignal 여러 개 생성
-> ExitDecisionAggregator가 등급별로 정리
-> 가장 강한 신호를 선택
-> 백테스트 결과로 채택/폐기
-> 검증 후에만 Paper/Live 연결 검토
```

## 공통 출력 등급

모든 매도 공식은 같은 출력 언어를 사용한다.

```text
ObserveOnly
  기록만 한다.

Warning
  이상 징후다. 아직 매도는 아니다.

ScaleOut
  일부 익절 또는 일부 축소.

ProtectProfit
  수익 보호 목적의 강한 일부/전량 매도.

FatalExit
  전량 매도 후보. 구조가 깨졌거나 버틸 이유가 사라진 상태.
```

공통 출력값:

```text
ExitRuleCode
Severity
SellWeightPercent
IsFullExit
Reason
TriggeredTime
ReferencePrice
StopBasePrice
EvidenceText
```

## 공통 입력값

백테스트와 실시간은 같은 입력 구조를 써야 한다.

```text
Position:
  EntryPrice
  CurrentPrice
  Quantity
  OpenQuantity
  ProfitRate
  MinProfitRate
  MaxProfitRate
  PostBuyHigh
  HighDrawdownRate
  FirstScaleOutDone
  SecondScaleOutDone

Bars:
  1m OHLCV + MA5/20/60 + VolumeMA
  3m OHLCV + MA5/20/60 + VolumeMA
  5m OHLCV + MA5/20/60 + VolumeMA
  10m/15m OHLCV + MA5/20/60 + VolumeMA

Realtime:
  0B market buy volume
  0B market sell volume
  0B buy ratio
  0D bid/ask quantity ratio

Structure:
  EntryMadiLow
  StopBasePrice
  TargetBasePrice
  MadiWaistLow/High
  SmallHillLow/High
```

## 매도전략 후보

### 0. Five Minute Base / One Minute Sell Test

현재 구현:

```text
Services/Strategies/Core/StrategyFiveMinuteOneMinuteSellTestEvaluator.cs
MainWindow.StrategyDebugSnapshots.cs -> SellTest
```

역할:

```text
실전 주문 연결이 아니라, 현재 후보 종목의 5분 기준/1분 트리거 매매에서
매도 후보가 어디서 뜨는지 스냅샷으로 확인하는 테스트 모드다.
```

시장 구분:

```text
KRX 종목 -> KRX 1분/5분 장부만 사용.
NXT 종목 -> NXT 1분/5분 장부만 사용.
KRX/NXT 분봉은 절대 섞지 않는다.
```

판정 입력:

```text
1m bars
5m bars
0B 최근 60초 시장가 매수/매도 체결 흐름
```

규칙:

```text
5분 최근 지지저가 이탈 + 1분 거래/거래대금/시장가매도 압력
-> StopAll 후보

5분 흐름 약화 + 1분 흐름 약화 + 압력 증가
-> ProtectProfit 후보

1분 약화 + 음봉 + 윗꼬리 + 거래/거래대금 증가
-> ScaleOut 후보

1분 또는 5분 약화만 있음
-> Warning, 주문 금지
```

스냅샷 출력:

```text
Storage/DebugSnapshots/*-strategy-minute.json
  Snapshots[].SellTest
```

금지:

```text
이 테스트 결과를 KiwoomTradingClient.SellAsync에 직접 연결하지 않는다.
Paper close도 이 테스트만으로 자동 처리하지 않는다.
```

### 1. Staged Profit Stop

현재 구현:

```text
Services/Strategies/Core/StrategyStagedExitEvaluator.cs
```

역할:

```text
실전형 기본 운영 세트.
분할익절, 트레일링, 본절회복, -1.2% 손절을 담당한다.
```

규칙:

```text
+1.0% -> 30% 매도
+2.0% -> 50% 매도
고점 대비 -1.0% -> 잔량 전량
-1.2% -> 전량 손절
5분 기준봉 저가 이탈 -> 전량 손절
15분 흐름 훼손 -> 전량 손절
-1.0% 이하 갔다가 본절 회복 -> 본절 매도
```

백테스트 목적:

```text
가장 단순한 생존형 매도 기준으로 삼는다.
다른 공식이 이 기준보다 나은지 비교한다.
```

### 2. Base Break Exit

현재 구현:

```text
Services/Strategies/Core/StrategyExitBreakEvaluator.cs
```

역할:

```text
손절 기준가, 마디 허리, 기준봉 저가 같은 생명선이 깨지는지 본다.
```

규칙:

```text
첫 이탈 -> Warning
거래량 작은 첫 이탈 -> TrapCandidate
기준가 회복 -> Recovered
두 번째 이탈 -> FatalExit
거래량 동반 이탈 -> FatalExit
반등 후 기준가 저항 -> FatalExit
```

백테스트 목적:

```text
고정 손절률보다 구조 손절이 더 좋은지 확인한다.
```

### 3. Linear Regression Exit

현재 구현:

```text
Services/Strategies/Core/StrategyLinearRegressionExitEvaluator.cs
```

역할:

```text
강좌 화살표 수식의 선형회귀 매도 신호를 비교대상으로 둔다.
```

규칙:

```text
CROSSDOWN(C, VL)
OR
CROSSDOWN(C, VL1)
```

백테스트 목적:

```text
가격 흐름 기반 화살표 매도 공식이 우리 구조 손절보다 빠른지/늦은지 확인한다.
```

### 4. Moving Average Step Exit

아직 구현 대기.

역할:

```text
5선, 20선, 60선을 단계별 매도 기준으로 쓴다.
```

규칙:

```text
수익권 + 5MA 이탈 -> ScaleOut
수익권 + 5MA 이탈 + 거래량 증가 음봉 -> ProtectProfit
20MA 이탈 -> 추가 축소 후보
60MA 이탈 -> FatalExit 후보
```

해석:

```text
5선은 탄력이다.
20선은 중간 흐름이다.
60선은 구조다.
```

백테스트 목적:

```text
가장 단순한 이평선 기반 매도 방어선이 어느 정도 유효한지 확인한다.
```

### 5. Volume Price Failure Exit

아직 구현 대기.

역할:

```text
거래가 늘었는데 가격이 못 올라가고 밀리는지 본다.
```

규칙:

```text
거래량 증가 + 윗꼬리 확대 -> ScaleOut
거래량 증가 + 음봉 전환 -> ProtectProfit
거래량 증가 + 5MA 이탈 -> ProtectProfit
거래량 증가 + 60MA 이탈 -> FatalExit
거래량 증가 + 소마디 저점 이탈 -> FatalExit
```

해석:

```text
돈이 들어오는데 못 오른다.
그 돈이 매수 돈이 아니라 매도 압력일 수 있다.
```

백테스트 목적:

```text
목표구간이 아직 남았더라도 힘이 꺾이면 일부 익절하는 기준을 검증한다.
```

### 6. Shoulder Exit

아직 구현 대기.

역할:

```text
고점을 넘어 어깨에서 꺾이는 구간을 잡는다.
매수 공식의 반대 구조다.
```

규칙:

```text
고점 이후 하락 마디 시작
5MA 하락 전환
현재가 60MA 아래
거래량 증가
음봉
소마디 저점 이탈
반등 시 60MA 또는 허리 저항
```

판정:

```text
조건 일부 충족 -> Warning
수익권 + 5MA 하락 전환 -> ScaleOut
60MA 이탈 + 거래량 증가 음봉 -> FatalExit
소마디 저점 이탈 -> FatalExit
```

백테스트 목적:

```text
고점 이후 어깨에서 전량 매도할 수 있는지 확인한다.
```

### 7. Heikin Ashi Trend Exit

아직 구현 대기.

역할:

```text
고점을 빠르게 잡기보다 추세가 실제로 꺾였는지 안정적으로 확인한다.
```

계산:

```text
HA_Close = (Open + High + Low + Close) / 4
HA_Open  = (PrevHA_Open + PrevHA_Close) / 2
HA_High  = Max(High, HA_Open, HA_Close)
HA_Low   = Min(Low, HA_Open, HA_Close)
```

규칙:

```text
수익권 + 하이켄아시 첫 음봉 -> ScaleOut
하이켄아시 음봉 + 5MA 이탈 -> ProtectProfit
하이켄아시 음봉 지속 + 60MA 이탈 -> FatalExit
하이켄아시 음봉 + 거래량 증가 + 소마디 저점 이탈 -> FatalExit
```

백테스트 목적:

```text
느리지만 안정적인 추세 청산 기준으로 쓸 수 있는지 확인한다.
```

### 8. High Volume Sell Pressure Scalp Exit

아직 구현 대기.

역할:

```text
고점 스캘핑 매도.
신고거래량, 시장가 매도 증가, 윗꼬리, 가격 정체를 함께 본다.
```

규칙:

```text
현재 1분봉 거래량 >= 최근 20봉 최고 거래량
AND 최근 30~60초 시장가 매도량 > 시장가 매수량
AND 매수 체결 비율 감소
AND 현재가가 봉 고가 대비 0.3~0.5% 이상 밀림
AND 윗꼬리 비율 >= 몸통의 50% 이상
```

판정:

```text
수익권이면 ProtectProfit
목표구간 근처면 FatalExit 가능
손실권이면 단독 사용 금지, 구조 손절과 결합
```

백테스트 목적:

```text
하이켄아시보다 빠르게 고점 매도 후보를 잡을 수 있는지 확인한다.
```

### 9. Volume TradeValue Failure Exit

아직 구현 대기.

역할:

```text
거래량과 거래대금이 동시에 늘었는데 가격이 전진하지 못하는지 본다.
거래량은 손바뀜이고, 거래대금은 실제 돈의 크기다.
둘 다 커졌는데 가격이 못 가면 매물 출회 또는 세력 이탈 후보로 본다.
```

공통 입력:

```text
CurrentVolume
CurrentTradingValue
VolumeMA5
VolumeMA20
VolumeMA60
TradeValueMA5
TradeValueMA20
TradeValueMA60
Open
High
Low
Close
PreviousClose
CurrentPrice
PostBuyHigh
TargetPrice
```

핵심 상태:

```text
VolumeValueExpansion:
  VolumeMA5 > VolumeMA20
  AND TradeValueMA5 > TradeValueMA20

HeavyMoneyCandle:
  CurrentTradingValue >= TradeValueMA20 * 2.0
  OR CurrentTradingValue is highest in recent 20 bars

MoneyButNoAdvance:
  CurrentTradingValue 증가
  AND Close <= PreviousClose 또는 Close가 High에서 크게 밀림

MoneyTopTail:
  CurrentTradingValue 증가
  AND UpperTailRatio >= 0.5
```

매도 규칙:

```text
수익권
AND VolumeValueExpansion
AND MoneyTopTail
-> ScaleOut

수익권
AND HeavyMoneyCandle
AND MoneyButNoAdvance
-> ProtectProfit

목표가 근처
AND HeavyMoneyCandle
AND UpperTailRatio >= 0.5
-> ProtectProfit 또는 FatalExit

5MA 이탈
AND CurrentTradingValue >= TradeValueMA20 * 1.5
AND 음봉
-> ProtectProfit

60MA 이탈
AND CurrentTradingValue >= TradeValueMA20 * 1.5
AND 음봉
-> FatalExit
```

해석:

```text
거래량이 늘었다 = 많이 사고팔았다.
거래대금이 늘었다 = 큰돈이 들어왔다.
큰돈이 들어왔는데 가격이 못 오른다 = 누군가 강하게 팔고 있다.
큰돈이 들어왔는데 윗꼬리가 생긴다 = 위에서 물량이 나온다.
큰돈이 들어왔는데 5MA를 깬다 = 수익 보호.
큰돈이 들어왔는데 60MA를 깬다 = 구조 훼손.
```

백테스트 목적:

```text
거래량만 보는 매도보다 거래대금 동시 확인이 더 좋은지 검증한다.
신고거래량 매도압력 공식과 비교해 빠른 청산/늦은 청산 차이를 본다.
```

### 10. Volume Dry-Up Hold Filter

아직 구현 대기.

역할:

```text
가격은 조금 밀리지만 거래량/거래대금이 줄어드는 정상 눌림인지 확인한다.
이 경우 매도 신호를 약화시키거나 보류한다.
```

규칙:

```text
가격이 5MA 근처까지 눌림
AND VolumeMA5 < VolumeMA20
AND TradeValueMA5 < TradeValueMA20
AND 소마디 저점 미이탈
-> 매도 보류 또는 ObserveOnly

가격이 5MA를 살짝 이탈
AND 거래량/거래대금 감소
AND 다음 봉에서 회복
-> TrapCandidate, 매도 보류
```

해석:

```text
거래가 줄며 밀리는 것은 정상 눌림일 수 있다.
거래가 늘며 밀리는 것은 위험이다.
```

백테스트 목적:

```text
너무 빨리 파는 것을 줄인다.
좋은 눌림을 매도 신호로 오판하지 않게 한다.
```

### 11. Time No-Go Exit

아직 구현 대기.

역할:

```text
매수 후 바로 가지 못하는 종목을 정리한다.
```

규칙:

```text
매수 후 N분 경과
AND 수익률 <= 0
AND 1분/5분 거래량 재증가 없음
-> ProtectProfit 또는 FatalExit
```

백테스트 목적:

```text
기회비용과 질질 끌리는 손실을 줄일 수 있는지 확인한다.
```

### 12. Absolute Base Price Stop Exit

아직 구현 대기.

역할:

```text
가까운 지지선이 아니라, 손절되어도 인정할 수 있는 절대 기준가를 찾는다.
절대 기준가가 없거나 손절폭이 너무 넓으면 매도 공식 이전에 진입 자체를 막는 필터로도 사용한다.
```

핵심 구분:

```text
일반 기준가:
  매수/지지/저항 판단에 쓰는 가격.
  한 번 흔들릴 수 있으므로 단독 전량 손절선으로 쓰지 않는다.

절대 기준가:
  깨지면 매수 시나리오가 틀렸다고 인정할 수 있는 가격.
  전량 손절 기준 후보.
```

일반 기준가가 절대 기준가로 승격되는 조건:

```text
대량 거래 장대봉의 시작점
지지/저항이 여러 번 반복된 가격대
기준 마디 허리 또는 마디 저점
거래량 동반 지지 확인 후 새로 생긴 기준가
```

매도 규칙:

```text
CurrentPrice < AbsoluteBasePrice
AND 거래량 동반 이탈
-> FatalExit

CurrentPrice < AbsoluteBasePrice
AND 두 번째 이탈
-> FatalExit

CurrentPrice < GeneralBasePrice
AND 첫 이탈
-> Warning 또는 TrapCandidate
```

백테스트 목적:

```text
가까운 지지선 손절과 절대 기준가 손절의 차이를 비교한다.
손절이 너무 잦은지, 늦지만 생존력이 좋은지 확인한다.
```

### 13. Rebound Escape Exit

아직 구현 대기.

역할:

```text
손절을 미루는 공식이 아니라, 이미 시나리오가 깨진 뒤 반등에서 탈출하는 예외 대응이다.
```

사용 조건:

```text
강한 기준 마디가 있었다.
중요 기준가 또는 허리 기준가를 거래량 동반으로 강하게 이탈했다.
매수 위치가 단기 고점 또는 트랩에 가깝다.
즉시 손절하지 못했거나, 계획상 반등 탈출 모드가 켜졌다.
```

매도 규칙:

```text
ReboundEscapeMode
AND 반등이 기준가/허리/5MA/20MA 근처에서 저항
-> FatalExit

ReboundEscapeMode
AND 본전 근처 회복
AND 재돌파 실패
-> ProtectProfit 또는 FatalExit

ReboundEscapeMode
AND 기준가 강한 회복
AND 거래량 재유입
-> 매도 보류, 재진입/유지 후보
```

백테스트 목적:

```text
기본 손절보다 늦지만 손실을 줄이는 탈출 공식으로 유효한지 확인한다.
반등 기다림이 손실 확대인지, 실제 탈출 기회인지 비교한다.
```

### 14. Stop Line Ratchet Manager

아직 구현 대기.

역할:

```text
손절선을 고정값이 아니라 상태값으로 관리한다.
단, 손절선은 원칙적으로 불리한 방향으로 낮추지 않는다.
새 절대 기준가가 생겼을 때만 손절선을 올린다.
```

관리 규칙:

```text
매수 직후:
  InitialStopPrice 고정.

수익권 진입:
  1차 익절 후 StopPrice를 본전 또는 새 절대 기준가로 상향.

새 기준 마디 발생:
  새 허리/마디저점이 절대 기준가로 승격되면 StopPrice 상향.

고가놀이:
  거래량 감소 + 고가권 유지면 StopPrice 유지 또는 소폭 상향 후보.

힘없는 늘어짐:
  StopPrice 도달 전이라도 ReboundEscapeMode 또는 TimeNoGoExit 후보.

금지:
  손절폭이 부담스럽다는 이유로 StopPrice를 아래로 완화하지 않는다.
```

백테스트 목적:

```text
고정 손절선과 상향식 손절선 관리의 수익 보호 효과를 비교한다.
손절선을 너무 빨리 올려 정상 눌림을 자르는지 확인한다.
```

### 15. Resistance Target Profit Exit

아직 구현 대기.

역할:

```text
상위 캔들/분봉 기준가를 목표 저항으로 보고, 목표구간 근처에서 힘이 약해지면 일부 또는 전량 익절한다.
```

대상 기준가:

```text
일봉 기준봉 고가
전고점
상위 마디 허리 저항
120MA/240MA 근처 저항
거래량 터진 캔들의 고점/저점
여러 번 지지/저항이 반복된 가격대
```

매도 규칙:

```text
TargetPrice 근처 도달
AND 거래량/거래대금 증가
AND 윗꼬리 또는 가격 정체
-> ScaleOut 또는 ProtectProfit

TargetPrice 근처 도달
AND 시장가 매도 체결량 증가
AND 현재가가 고가에서 밀림
-> ProtectProfit

TargetPrice 돌파 실패가 2회 이상 반복
-> ProtectProfit 또는 FatalExit
```

백테스트 목적:

```text
고정 +1/+2/+3% 익절보다 구조적 목표가 익절이 더 좋은지 확인한다.
목표가 앞에서 밀리는 종목을 늦게 팔지 않는지 검증한다.
```

### 16. Reverse Chart Mirror Exit

아직 구현 대기.

역할:

```text
매도 신호를 반대 방향 매수 급소처럼 계산한다.
손절/익절 판단의 심리적 편향을 줄이는 검증용 공식이다.
```

해석:

```text
원본 차트의 손절 급소 = 반전 차트의 매수 급소.
원본 차트의 익절 후보 = 반전 차트의 예측 매수 후보.
```

매도 규칙 후보:

```text
반전 차트에서 AF/VL/눌림 돌파 매수 급소 발생
-> 원본 차트에서는 ProtectProfit 또는 FatalExit 후보

반전 차트에서 기준 마디 허리 지지 후 2차 상승 초입
-> 원본 차트에서는 하락 마디 시작 후보
```

백테스트 목적:

```text
롱 매수 공식의 반대 구조가 실제 청산 공식으로 쓸 수 있는지 비교한다.
사람 눈에는 버티고 싶은 구간을 공식이 더 객관적으로 자르는지 확인한다.
```

### 17. Style-Aware Exit Guard

아직 구현 대기.

역할:

```text
같은 종목, 같은 가격이라도 진입 스타일에 따라 매도 기준을 다르게 적용한다.
단타로 산 종목을 추세처럼 버티지 않게 막는다.
```

스타일별 원칙:

```text
Scalp:
  1분/틱 체결 약화와 시장가 매도 증가를 빠르게 반영.
  손절 짧음. 오래 버티지 않음.

DayTrade:
  1분 트리거 실패는 경고, 5분 기준 훼손은 전량.
  목표구간에서는 분할익절 우선.

Madi:
  기준 마디 허리/저점이 생명선.
  정상 눌림은 버티지만 마디 훼손은 전량.

Trend:
  작은 흔들림보다 15분/상위 마디 훼손을 우선.
  단, 수익 보호용 분할익절은 허용.
```

백테스트 목적:

```text
모든 포지션에 같은 매도 공식을 적용할 때보다,
진입 의도별 매도 공식이 더 안정적인지 확인한다.
```

## 하이브리드 최종 판정 원칙

여러 공식이 동시에 신호를 내면 가장 강한 등급을 따른다.

```text
FatalExit > ProtectProfit > ScaleOut > Warning > ObserveOnly
```

예외:

```text
손실권 FatalExit는 전량 우선.
수익권 ProtectProfit은 분할 또는 전량 선택 가능.
Warning은 단독 주문 금지.
ObserveOnly는 기록만.
```

같은 등급이면 아래 우선순위를 둔다.

```text
1. 구조 이탈
2. 거래량 동반 이탈
3. 시장가 매도 압력
4. 이평선 이탈
5. 보조 공식
```

## 백테스트 비교 지표

각 매도 공식은 같은 매수 진입 기록을 대상으로 비교한다.

```text
ExitRuleCode
EntryTime
ExitTime
EntryPrice
ExitPrice
ProfitRate
MaxProfitBeforeExit
DrawdownFromHigh
MAE
MFE
HoldingMinutes
MissedUpsideAfterExit
SavedLossAfterExit
ExitReason
```

보고 싶은 질문:

```text
너무 빨리 팔았나?
너무 늦게 팔았나?
전량매도 대신 일부익절이 나았나?
하이켄아시는 늦지만 안정적인가?
신고거래량 매도압력은 고점을 잘 잡는가?
선형회귀 화살표는 우리 구조 손절보다 좋은가?
5MA 일부 매도는 수익을 보호했나, 수익을 잘랐나?
```

## 프로그램화 대기 원칙

현재 단계에서는 실전 매도 주문에 연결하지 않는다.

허용:

```text
문서화
백테스트 계산기
Paper 기록
Progress 표시
스냅샷 검증
텔레그램 알림
```

금지:

```text
KiwoomTradingClient.SellAsync 직접 연결
StrategyExitAlert의 실주문 경로 변경
보유 종목 전체를 평균단가만 보고 fallback 매도
검증되지 않은 보조 신호 단독 전량매도
```

## 다음 작업 순서

```text
1. ExitSignal 공통 모델 정의
2. MovingAverageStepExit 계산기 작성
3. VolumePriceFailureExit 계산기 작성
4. HeikinAshiTrendExit 계산기 작성
5. HighVolumeSellPressureScalpExit 계산기 작성
6. AbsoluteBasePriceStopExit 계산기 작성
7. ReboundEscapeExit 계산기 작성
8. StopLineRatchetManager 작성
9. ResistanceTargetProfitExit 계산기 작성
10. ReverseChartMirrorExit 검증 계산기 작성
11. StyleAwareExitGuard 작성
12. 기존 백테스트 엔진에 ExitRuleCode별 비교 출력 추가
13. 결과 보고서에서 채택/보류/폐기 표시
```
