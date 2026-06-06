이 파일은 UTF-8 기준으로 작성됨.

# NoBuy / Caution 필터 후보 정리

작성일: 2026-06-06

## 목적

이 문서는 매수 신호를 강화하기 위한 문서가 아니다.

목적은 매수 후보가 들어왔을 때, 먼저 "안 살 이유"와 "조심해야 할 이유"를 태그로 표시하는 것이다.

핵심 원칙:

- 좋은 신호를 억지로 만들지 않는다.
- 수익 조건은 건드리지 않는다.
- 손절이 먼저 나올 가능성이 큰 후보를 먼저 태그로 걸러본다.
- 처음에는 실주문 필터가 아니라 백테스트 진단용 태그로만 사용한다.

## 적용 위치

우선 적용 대상은 백테스트 진단이다.

실시간 프로그램의 매수 차단 로직이나 실주문 RiskGuard에는 바로 연결하지 않는다.

권장 흐름:

1. 매수 후보 발생
2. NoBuy / Caution 태그 계산
3. 손절 먼저 나온 종목과 수익 먼저 준 종목의 태그 분포 비교
4. 잘 맞는 태그만 감점 또는 차단 후보로 승격
5. 충분히 검증 후 실시간 Progress / 알림에 표시

## 기본 변수

- `O`: 시가
- `H`: 고가
- `L`: 저가
- `C`: 종가 또는 현재가
- `V`: 거래량
- `TV`: 거래대금
- `M5`: 5이평
- `M10`: 10이평
- `M20`: 20이평
- `M60`: 60이평
- `M200`: 200이평
- `V20`: 거래량 20평균
- `TV20`: 거래대금 20평균
- `TV3`: 거래대금 3평균

보조 계산:

- `ClosePos = (C - L) / (H - L)`
- `UpperTailRatio = (H - Max(O, C)) / (H - L)`
- `BodyRatio = Abs(C - O) / (H - L)`

## Hard Exclude 후보

아래 태그는 이름상 Hard Exclude지만, 현재 단계에서는 바로 매수 금지로 쓰지 않는다.

백테스트에서 손절 먼저 나온 종목에 얼마나 자주 붙는지 확인한 뒤 단계적으로 승격한다.

### 돈은 들어왔는데 가격이 못 가는 경우

- `NoBuy_MoneyInButNoPrice`
  - 거래대금이 평균보다 크게 늘었는데 고가 갱신이 안 되는 경우
- `NoBuy_MoneyInButCloseWeak`
  - 최근 거래대금은 늘었는데 종가가 직전 종가 이하인 경우
- `NoBuy_LowPriceEfficiency`
  - 거래대금은 큰데 가격 전진률이 너무 낮은 경우

### 하락에 돈이 실리는 경우

- `NoBuy_DownBarMoneyIncrease`
  - 음봉인데 거래대금이 평균보다 크게 증가한 경우
- `NoBuy_DownBarMoneyIncrease2`
  - 종가가 직전보다 낮고 거래대금이 직전보다 증가한 경우

### 돌파 실패

- `NoBuy_PrevHighBreakFail`
  - 고가는 전일고가를 넘었지만 종가가 전일고가 아래로 밀린 경우
- `NoBuy_PrevHighBreakFailAfterSignal`
  - 전일고가 돌파 신호 후 1~2봉 안에 다시 전일고가 아래로 밀린 경우

### 이평 지지 실패

- `NoBuy_MA5SupportFail`
  - 5이평을 터치했지만 종가가 5이평 아래인 경우
- `NoBuy_MA10SupportFail`
  - 10이평을 터치했지만 종가가 10이평 아래인 경우
- `NoBuy_BelowMA5NoRecovery`
  - 5이평 아래에서 회복하지 못하는 경우
- `NoBuy_BelowMA5ThreeBars`
  - 3봉 연속 5이평 아래인 경우

### 봉 품질 약화

- `NoBuy_LongUpperTail`
  - 윗꼬리가 긴 경우
- `NoBuy_LongUpperTailWithMoney`
  - 거래대금이 큰데 윗꼬리가 긴 경우
- `NoBuy_WeakClose`
  - 종가 위치가 봉 하단에 가까운 경우
- `NoBuy_CloseBelowMiddle`
  - 종가가 봉 중심 아래인 경우
- `NoBuy_HigherHighWeakClose`
  - 고가는 높였지만 종가는 직전보다 낮은 경우

### 눌림 과다 / 마디 훼손

- `NoBuy_PullbackTooDeep`
  - 20이평과 전일고가 아래로 깊게 밀린 경우
- `NoBuy_PullbackBreakBaseHalf`
  - 기준봉 허리 아래로 내려가고 10이평도 회복하지 못한 경우

### 거래대금 건조

- `NoBuy_TradeValueBelowAverage`
  - 거래대금이 20평균보다 낮은 경우
- `NoBuy_TradeValueDryUp`
  - 거래대금이 급감하고 직전 고가도 회복하지 못한 경우
- `NoBuy_TradeValueDryUp2`
  - 최근 거래대금이 평균보다 낮고 5이평 아래인 경우

### 기준봉 자체 문제

- `NoBuy_StaleBaseCandle`
  - 기준봉이 너무 오래된 경우
- `NoBuy_BaseCandleLongUpperTail`
  - 기준봉 윗꼬리가 긴 경우
- `NoBuy_BaseCandleWeakClose`
  - 기준봉 종가 위치가 약한 경우

## Caution 후보

Caution은 매수 금지보다 감점, 비중 축소, 추가 확인 용도로 본다.

- `NoBuy_WeakPrevHighBreakout`
- `NoBuy_WeakTriggerBreakout`
- `NoBuy_RebreakVolumeWeak`
- `NoBuy_RebreakTradeValueWeak`
- `NoBuy_HighTurnoverNoBreakout`
- `NoBuy_RankEntryFail`
- `NoBuy_RankEntryPrevHighFail`
- `NoBuy_OverheadMA20Resistance`
- `NoBuy_OverheadMA60Resistance`
- `NoBuy_OverExtendedChase`
- `NoBuy_OverExtendedChase2`
- `NoBuy_WeeklyResistanceNear`
- `NoBuy_NoFollowThrough`
- `NoBuy_NoFollowThrough2`
- `NoBuy_DownMoneyDominant`

## 2026-06-06 1차 진단 결과

대상:

- 백테스트 런: `SORON_five_preday_range_half_rsi2_trigger_20260606082002`
- 진입식: `A=PREDAYHIGH()-PREDAYLOW(); B=DAYOPEN()+A*0.5; RSI2>50; CROSSUP(C,B)`
- 보유 판정: 다음 거래일 11시까지, 전일종가 손절 또는 3% 목표 선착순 판정

결과 요약:

- 성공군 `TARGET_3_FIRST`: 41건 중 `NO_BUY` 태그 10건
- 손절군 `PREV_CLOSE_STOP_FIRST`: 20건 중 `NO_BUY` 태그 9건
- 애매군 `NO_TARGET_OR_STOP`: 5건 중 `NO_BUY` 태그 2건

해석:

- 현재 태그 묶음은 손절군 일부를 잡지만 성공군도 꽤 자른다.
- 따라서 이 수식을 그대로 매수 금지로 쓰면 좋은 신호까지 죽일 수 있다.
- 특히 `LongUpperTail`, `LongUpperTailWithMoney`, `CloseBelowMiddle`은 손절군과 성공군 양쪽에 모두 나타난다.
- 현 단계에서는 Hard Exclude가 아니라 점수/태그/복기 기준으로 먼저 써야 한다.

생성된 진단 파일:

- `Storage/Backtests/Runs/SORON_five_preday_range_half_rsi2_trigger_20260606082002/no_buy_filter_tag_diagnostic.csv`

## 2026-06-06 반대방향 진단 결과

NoBuy의 반대방향도 같이 확인했다.

목적:

- 안 살 이유만 보지 않는다.
- 그래도 관찰을 이어갈 이유가 있는지도 같이 본다.
- 단, 이 값은 매수 실행 사유가 아니라 NoBuy 태그를 해석하는 보조 점수다.

반대방향 태그 후보:

- `Positive_StrongBGap`
  - 전일폭 절반선 `B` 돌파 여유가 충분한 경우
- `Positive_RSI2Strong`
  - RSI(2)가 강한 경우
- `Positive_CloseNearHigh`
  - 종가가 봉 상단에 위치한 경우
- `Positive_ShortUpperTail`
  - 윗꼬리가 짧은 경우
- `Positive_UsefulBody`
  - 몸통이 의미 있게 나온 경우
- `Positive_ShortStructureRisk`
  - 기준봉 저가 또는 구조 손절선까지 거리가 짧은 경우
- `Positive_AboveMA200NotOverheated`
  - 200이평 위지만 과열 이격은 아닌 경우
- `Positive_BetterPullbackEntryExists`
  - 첫 돌파 후 더 짧은 리스크의 눌림 재진입 후보가 있는 경우

진단 결과:

- 성공군 `TARGET_3_FIRST`: 43건 / 평균 Positive 4.14 / 평균 Weak 1.09 / 평균 Net +3.05
- 손절군 `PREV_CLOSE_STOP_FIRST`: 21건 / 평균 Positive 4.38 / 평균 Weak 1.43 / 평균 Net +2.95
- 애매군 `NO_TARGET_OR_STOP`: 5건 / 평균 Positive 3.80 / 평균 Weak 1.80 / 평균 Net +2.00

해석:

- 반대방향 태그만으로 성공군과 손절군이 분리되지 않는다.
- 손절군도 진입 시점에는 좋은 봉처럼 보이는 경우가 많다.
- 따라서 현재 매수식의 문제는 "진입봉 품질" 하나로 해결되지 않는다.
- 핵심 확인 지점은 진입 후 1~3봉의 유지력이다.

생성된 진단 파일:

- `Storage/Backtests/Runs/SORON_five_preday_range_half_rsi2_trigger_20260606082002/positive_structure_tag_diagnostic.csv`

현재 결론:

- Positive 태그는 매수 확정용이 아니다.
- Positive 태그는 NoBuy 태그를 상쇄하는 보조 근거로만 사용한다.
- Positive가 높아도 이후 유지 실패가 나오면 매수하면 안 된다.
- 다음 검증은 진입 이후 `NoFollowThrough`, `B선 재이탈`, `전일고가 재이탈`, `하락 거래대금 증가`, `1~3봉 고점 갱신 실패`를 중심으로 한다.

## 다음 검증 순서

1. 손절 먼저 나온 종목만 따로 모은다.
2. 각 손절 종목의 태그와 차트 PNG를 같이 본다.
3. 성공군에도 많이 붙는 태그는 감점만 준다.
4. 손절군에 집중되고 성공군에는 적은 태그만 차단 후보로 승격한다.
5. `첫 CROSSUP`은 매수 신호가 아니라 기준봉 후보로 보고, 매수는 눌림 지지 또는 재돌파에서 다시 검증한다.

## 현재 결론

NoBuy / Caution 수식 방향은 맞다.

하지만 아직은 실매매 차단기가 아니라, 손절 먼저 나온 종목을 설명하기 위한 태그 사전이다.

프로그램에 바로 연결한다면 다음 순서가 안전하다.

1. 백테스트 진단 태그
2. Progress 표시용 태그
3. 알림 문구 태그
4. Paper Trading 감점
5. 충분히 검증 후 RiskGuard 보조 조건

최종 원칙:

좋은 신호를 찾기 전에, 손절이 먼저 나올 이유를 먼저 찾는다.
