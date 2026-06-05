// 이 파일은 UTF-8 기준으로 작성됨

# 구조적 손절·익절·보유 기준 점검

작성일: 2026-06-06

이 문서는 강좌 문서의 손절, 익절, 보유, 매수 판단 언어를 현재 `TradingDashboard`의 전략 장부와 비교해 빠진 부분을 정리한 적용 준비 문서다.

목적은 바로 실주문에 연결하는 것이 아니다.
강좌에서 말한 “손절은 어디서, 익절은 어디서, 보유는 왜 가능한가”를 프로그램이 읽을 수 있는 함수와 상태값으로 바꾸기 위한 사전 점검이다.

## 최종 원칙

매수는 안 살 이유가 없는 자리에서만 시도한다.

매도는 팔 이유가 생겼는지, 아직 안 팔 이유가 남아 있는지를 동시에 본다.

손절은 고정 N%가 아니라 매수 시나리오가 깨진 가격에서 한다.

익절은 고정 N%가 아니라 목표 공간, 저항, 거래량 증가 후 가격 정체, 윗꼬리, 매도 체결 우세 같은 구조 변화에서 한다.

보유는 “아직 오를 것 같다”가 아니라, 지켜야 할 기준가와 마디가 살아 있고 돈이 빠져나간 증거가 부족할 때만 한다.

## 현재 이미 있는 것

### 1. 기준가 이탈 판정

현재 구현:

```text
Services/Strategies/Core/StrategyExitBreakEvaluator.cs
```

강좌와 맞는 부분:

- 첫 이탈
- 거래량 작은 첫 이탈은 트랩 후보
- 회복
- 두 번째 이탈
- 거래량 동반 강한 이탈
- 반등 후 기준가 저항

이 판정기는 강좌의 “기준가를 한 번 깼다고 바로 확정하지 말고, 거래량 동반 이탈·두 번째 이탈·반등 저항을 보라”와 방향이 맞다.

현재 한계:

- 기준가가 일반 기준가인지 절대 기준가인지 분류가 약하다.
- 여러 기준가 중 어떤 기준가를 손절 기준으로 승격할지 별도 계산이 약하다.
- “강한 이탈”에 거래대금, 매도 체결 우세, 회전율 대비 고가 갱신 실패가 아직 충분히 들어가지 않았다.

### 2. 단계형 매도 평가

현재 구현:

```text
Services/Strategies/Core/StrategyStagedExitEvaluator.cs
```

현재 판정:

- 1분 약화: 축소 경고
- 5분 약화: 전량 손절 후보
- 15분 약화: 전량 손절 후보
- 5분 기준봉 저가 이탈: 전량 손절 후보

강좌와 맞는 부분:

- 작은 시간틀은 조기 경고
- 기준 시간틀 손상은 전량 대응
- 큰 시간틀 훼손은 버리는 기준

현재 한계:

- 이평선 약화가 대부분 `종가 < MA5` 중심이라, 기준마디/허리/돌파봉 저가/첫눌림 저가와 연결이 약하다.
- 수익 중 보유와 익절을 나누는 구조가 아직 부족하다.
- “거래대금은 계속 들어오는데 가격이 못 간다”는 주춤 신호가 없다.

### 3. 선형회귀 매도 비교 신호

현재 구현:

```text
Services/Strategies/Core/StrategyLinearRegressionExitEvaluator.cs
```

역할:

- 강좌 수식 기반 매도 후보를 백테스트 비교용으로 둔다.

현재 위치:

- 실전 매도 조건이 아니라 비교 신호다.
- 구조 손절과 충돌하지 않게 보조/비교용으로 유지한다.

### 4. 전략분봉 장부

현재 입력:

```text
StrategyMinuteFrameSnapshot
```

포함:

- 현재봉/직전 확정봉 시고저종
- 현재봉/직전 확정봉 거래량, 거래대금
- MA5, MA10, MA20, MA60, MA200, MA240, MA480
- 최근 20봉 고가/저가/최고종가/최저종가
- 최근 20봉 거래량/거래대금

강점:

- 손절·익절·보유 판단에 필요한 뼈대 데이터는 대부분 있다.

부족:

- 거래량 MA5/20/60이 공통 스냅샷 필드로 없다. 일부 전략에서 개별 계산만 한다.
- 거래대금 MA5/20/60이 공통 스냅샷 필드로 없다.
- 체결강도, 체결속도, 시장가 매수/매도 60초 흐름, 호가 잔량 변화는 전략 공통 입력으로 아직 정리되지 않았다.

## 강좌 기준으로 빠진 판단

### 1. 손절선 먼저 확정

강좌 원칙:

```text
손절은 진입 후가 아니라 진입 전에 먼저 정한다.
손절선은 가까운 지지선이 아니라, 깨지면 시나리오가 틀렸다고 인정할 수 있는 절대 기준가다.
```

현재 부족:

- 매수 후보마다 `StopBasePrice`는 있으나, 이 값이 왜 절대 기준가인지 설명/등급이 약하다.
- 일반 기준가와 절대 기준가가 같은 필드처럼 쓰일 위험이 있다.

필요 함수:

```text
ClassifyStrategyBasePrice
ResolveAbsoluteStopBasePrice
ValidateStopBeforeEntry
```

필요 출력:

```text
GeneralBase
StopCapableBase
AbsoluteStopBase
StopBaseReason
NoEntryBecauseStopTooFar
```

### 2. 기준가 승격

강좌 원칙:

일반 기준가도 대량 거래 장대봉, 반복 지지/저항, 회복 후 재지지로 절대 기준가로 승격될 수 있다.

현재 부족:

- 기준가 승격 기준이 명시 함수로 없다.

필요 함수:

```text
EvaluateBasePricePromotion
CountSupportTouches
CountResistanceRejections
DetectVolumeBackedBaseCreation
```

승격 후보:

- 5분 기준봉 저가
- 5분 기준봉 중심
- 첫눌림 지지봉 저가
- 전일고가 돌파 후 재지지 가격
- 10분/15분 기준마디 허리

### 3. 마디 안의 허리

강좌 원칙:

허리는 산술 50%가 아니라 마디 중심 부근에서 실제 지지/저항이 모인 영역이다.

현재 부족:

- `MadiWaistZone` 용어는 문서에 있지만, 실제 공통 계산기는 약하다.
- 허리 위 보유, 허리 아래 약화, 허리 회복, 허리 반등 실패가 아직 전략별로 통일되지 않았다.

필요 함수:

```text
CalculateMadiWaistZone
EvaluateMadiAliveScore
DetectWaistBreak
DetectWaistRecovery
DetectWaistReboundRejection
```

입력:

- 기준마디 시작가/고가/저가
- 마디 안 전고점/전저점 클러스터
- 거래량이 몰린 가격대
- 지지/저항 반복 횟수

출력:

```text
WaistLow
WaistHigh
WaistScore
WaistAlive
WaistBroken
WaistRecovered
WaistRejected
```

### 4. 작은 기준선 안의 작은 기준선

사용자 정의:

큰 기준마디 안에 작은 5분 기준봉이 있고, 그 안에 1분 트리거 기준선이 있다.

현재 부족:

- 큰 시간틀과 작은 시간틀의 기준가 계층이 명확히 한 묶음으로 출력되지 않는다.

필요 구조:

```text
StructuralLevelSet
```

계층:

```text
DailyGateBaseCandle
HigherFrameMadiWaist
StrategyFrameBaseLow
StrategyFrameBaseCenter
FirstPullbackSupportLow
BreakoutCandleLow
TriggerFrameMa60
TriggerSmallHillHigh
```

용도:

- 위쪽 기준선은 방향/보유 판단
- 중간 기준선은 손절/축소 판단
- 아래쪽 기준선은 즉시 위험 판단

### 5. 강한 이탈의 강화

현재 `StrategyExitBreakEvaluator`는 거래량 평균 대비 이탈을 본다.

추가해야 할 강한 이탈 후보:

```text
종가 기준 기준가 이탈
+ 거래량 MA20 대비 1.8배 이상
+ 거래대금 MA20 대비 1.8배 이상
+ 음봉 마감
+ 현재봉 종가가 저가 부근
+ 시장가 매도체결량이 매수체결량보다 우세
+ 반등 시 같은 기준가에서 저항
```

필요 함수:

```text
DetectStrongBaseBreak
DetectVolumeBackedBreak
DetectTradingValueBackedBreak
DetectSellPressureBreak
DetectCloseNearLow
```

주의:

첫 이탈은 트랩일 수 있다.
거래량이 작고 바로 회복하면 전량 매도 근거가 아니다.

### 6. 보유 이유 계산

현재 매도 신호는 있지만 “아직 보유할 이유”를 세는 함수가 약하다.

보유 이유:

- 기준가 위 종가 유지
- 허리 위 지지
- 첫눌림 저가 미이탈
- 돌파봉 저가 미이탈
- 5분 MA5/MA10 지지
- 1분 흔들림은 있으나 5분 구조 생존
- 거래량/거래대금이 줄지 않거나 재증가
- 시장가 매수체결 우세
- 호가 잔량비가 급격히 무너지지 않음
- 고가 갱신 또는 고가 근처 유지

필요 함수:

```text
EvaluateHoldReasons
CalculateHoldScore
DetectMoneyStillIn
DetectPriceNotFallingDespiteTurnover
DetectHighRenewalOrNearHighHold
```

출력:

```text
HoldScore
HoldReasons[]
ExitReasons[]
DominantDecision = Hold / Warning / ScaleOut / FullExit
```

### 7. 익절과 일부 축소

강좌와 사용자 방향:

익절은 목표가에 닿았거나, 목표 공간이 줄었거나, 거래가 늘었는데 못 가는 경우에 일부 줄인다.

현재 부족:

- 목표 구간 계산과 “주춤” 신호가 약하다.

익절 후보:

- 상위 저항/전고점 근처 도달
- 기준마디 폭의 0.5R, 1.0R 도달
- 가격이 목표권인데 거래대금 증가 후 고가 갱신 실패
- 윗꼬리 증가
- 시장가 매도 체결 증가
- 1분 약화지만 5분/15분은 살아 있음

필요 함수:

```text
ResolveTargetZone
CalculateRewardRiskByStructure
DetectTargetZoneApproach
DetectMoneyInButPriceStalled
DetectHighTurnoverNoHighRenewal
DetectUpperTailWithSellPressure
EvaluateScaleOutDecision
```

분류:

```text
ObserveOnly
Warning
ScaleOut
ProtectProfit
FatalExit
```

### 8. 회전율과 대장성

강좌/검색식 방향:

회전율, 거래대금 순위, 전일동시간대 대비 거래량 증가, 거래량회전율 순위는 “돈이 들어온다”의 증거다.

현재 부족:

- 회전율과 순위는 후보 장부/화면 정보에는 있으나, 실시간 매도/보유 판단 공통 입력으로 약하다.

필요 함수:

```text
EvaluateTurnoverContext
DetectHighTurnoverWithoutProgress
DetectLeaderMoneyFlowMaintained
```

해석:

- 회전율이 높고 고가를 갱신하면 보유/추가 관찰
- 회전율이 높은데 고가 갱신 실패하면 익절/축소 후보
- 회전율이 높은데 기준가 이탈하면 강한 이탈 후보

## 매수 쪽 빠진 준비

### 1. 매수 가능 상태와 패턴 신호 분리

강좌 원칙:

패턴은 마지막이다.
먼저 매매 가능 구간, 손절선, 목표 공간, 손익비가 있어야 한다.

현재 부족:

- 일부 백테스트 수식은 패턴 먼저 찾는 형태다.
- 실전 전략은 `NoBuyReasons`가 있으나 기준가/손익비/목표 공간을 더 엄격히 통합해야 한다.

필요 함수:

```text
EvaluateTradeContext
EvaluateNoBuyReasons
ValidateRewardRiskBeforeSignal
```

### 2. 돈이 들어온 곳의 위치 해석

검색식은 재료 전달자다.
프로그램은 그 돈이 일봉/분봉의 어디에 들어왔는지 요리해야 한다.

필요 판단:

- 일봉 기준봉인지
- 기준마디 초입인지
- 마디 허리 위인지
- 전고점 돌파인지
- 이미 목표권인지
- 5분/1분 소마디로 한 마디만 먹을 자리인지

필요 함수:

```text
ClassifyMoneyFlowLocation
ClassifyDailyMadiPosition
ClassifyIntradayMadiPosition
```

### 3. 거래량 이평

사용자 핵심:

거래가 늘었다가 줄었다가 다시 늘어나는지는 거래량 이평선으로 본다.

현재 상태:

- 차트에는 거래량 이평 표시가 있다.
- 일부 전략은 1분 거래량 MA5/20/60을 내부 계산한다.
- 공통 스냅샷에는 거래량 MA/거래대금 MA가 없다.

필요 추가:

```text
VolumeMa5
VolumeMa20
VolumeMa60
TradingValueMa5
TradingValueMa20
TradingValueMa60
```

우선순위:

1. 스냅샷/JSON에만 추가
2. Progress 표시
3. 백테스트 비교
4. 매도/보유 판정 편입

## 적용 우선순위

### 1단계: 문서와 함수명 고정

먼저 아래 공통 이름을 고정한다.

```text
StructuralLevelSet
ClassifyStrategyBasePrice
ResolveAbsoluteStopBasePrice
EvaluateHoldReasons
EvaluateScaleOutDecision
DetectMoneyInButPriceStalled
DetectHighTurnoverNoHighRenewal
DetectStrongBaseBreak
```

### 2단계: 데이터 보강

`StrategyMinuteFrameSnapshot`에 아래 후보를 추가 검토한다.

```text
VolumeMa5 / VolumeMa20 / VolumeMa60
TradingValueMa5 / TradingValueMa20 / TradingValueMa60
```

0B 실시간에서 아래 rolling 입력을 공통화한다.

```text
MarketBuyVolume60s
MarketSellVolume60s
MarketBuyRatio60s
TradeSpeed60s
```

### 3단계: 주문 없는 평가기

실주문 연결 없이 평가기만 만든다.

```text
StrategyStructuralHoldExitEvaluator
```

입력:

```text
StructuralLevelSet
StrategyMinuteSnapshotSet
RealtimeExecutionFlow
PositionState
```

출력:

```text
HoldScore
ExitScore
ScaleOutScore
FullExitScore
HoldReasons[]
ExitReasons[]
SuggestedAction
```

### 4단계: 스냅샷 검증

Codex 확인 스냅샷에 아래를 넣는다.

```text
StructuralLevels
HoldReasons
ExitReasons
ScaleOutReasons
StrongBreakEvidence
TargetZoneEvidence
```

### 5단계: 백테스트

매수 전략과 강제로 묶지 않고, 보유 중이라고 가정한 매도/보유 신호 테스트부터 한다.

검증 질문:

- 팔라고 한 자리가 실제로 구조 훼손인가?
- 팔지 말라고 한 자리가 이후 버틸 가치가 있었나?
- 일부 익절 신호가 고점 근처에서 너무 늦거나 빠르지 않은가?
- 강한 이탈이 첫 트랩을 너무 많이 팔지 않는가?

### 6단계: Paper 연결

Live Orders가 아니라 Paper/알림/Progress에만 연결한다.

### 7단계: 실주문 검토

실주문은 마지막이다.
RiskGuard, Live Orders, 중복 주문, 예산, 슬롯 수, 시장상태 신선도, KRX/NXT 주문 구분을 모두 통과해야 한다.

## 지금 당장 코드에 넣지 않을 것

- 고정 -1%, +3% 같은 단순 손절/익절 수식
- 첫 이탈 즉시 전량 매도
- 선형회귀 매도 신호 단독 실전 연결
- 볼린저/MA200 보조 신호 단독 매수
- 스냅샷 AI 판단을 주문으로 직접 연결

## 결론

현재 프로그램은 손절의 뼈대는 있다.

하지만 강좌 기준으로 보면 아직 세 부분이 부족하다.

1. 손절 기준가의 등급 분류
2. 보유 이유 계산
3. 목표권에서 주춤할 때 일부 익절하는 판단

다음 개발은 새 매수전략 추가보다 `구조적 보유/축소/전량매도 평가기`를 먼저 만드는 것이 맞다.

전략은 “사는 이유”만 있으면 부족하다.
보유 중인 종목이 지금도 살아 있는지, 일부 줄여야 하는지, 완전히 버려야 하는지를 같은 언어로 판단해야 한다.
