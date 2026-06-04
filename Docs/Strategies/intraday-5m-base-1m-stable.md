# Slot 6 - 당일 5분 기준봉 + 1분 안정형 트리거

## 목적

이 슬롯은 짧은 시공간에서 빠르게 연습하고 검증하기 위한 안정형 단타 전략이다.

공격형 돌파를 바로 추격하지 않고, 5분봉에서 작은 기준마디가 생겼는지 먼저 보고,
1분봉에서 다시 살아나는 신호가 확인될 때만 주문 후보로 올린다.

핵심은 다음이다.

```text
5분 기준봉
-> 5분 기준봉 허리/60선 지지
-> 10분/15분 상위 흐름이 너무 적대적이지 않음
-> 1분 MA5/MA60 회복
-> 1분 거래량 MA 확장
-> 1분 작은 언덕 돌파
-> 0B 매수 우위 + 0D 매수잔량 받침
-> exit-first 손익비 통과
```

## 위치

- 코드: `Services/Strategies/IntradayFiveMinuteStableScalp/IntradayFiveMinuteStableScalpStrategySlot.cs`
- 슬롯: `StrategySlotId.IntradayFiveMinuteStableScalp`
- UI: `Slot 6 · DAY 5m Base + 1m Stable`
- Progress 필터: `DAY 5m+1m`
- 기본값: OFF

기본 OFF인 이유는 이 전략이 빠른 연습/검증용이기 때문이다.
실전 연결은 Progress와 알림 검증 후 켠다.

## 데이터 요구

전략은 다음 분봉 장부가 준비되어야 평가된다.

```text
1분봉
5분봉
10분봉
15분봉
```

5분봉은 기준봉과 허리 지지 판단에 사용한다.
1분봉은 최종 진입 트리거에 사용한다.
10분봉과 15분봉은 상위 흐름이 지나치게 적대적인지 확인하는 보조 필터다.

## 5분 기준봉 정의

5분 기준봉은 당일 완성봉 중 아래 조건을 만족하는 최근 봉이다.

```text
양봉
몸통 등락률 0.8% 이상
5분봉 거래대금 5억 이상
종가가 고가 근처
5분 MA60 아래에 깊게 깔려 있지 않음
```

이 기준봉은 기존 KRX 일봉 기준봉을 대체하지 않는다.
화면 목록 게이트의 `GateBaseCandle*` 값도 덮지 않는다.

## 안정형 트리거

안정형은 “쏘는 봉”을 바로 잡는 것이 아니라, 작은 마디가 유지되는지 본다.

필수 흐름:

```text
5분 기준봉 허리 위 지지
5분 MA5/MA20 흐름 훼손 없음
1분 종가가 MA60 위로 회복
1분 MA5가 MA60 근처 또는 위
1분 양봉 전환
1분 거래량 MA5가 MA20 이상
1분 최근 작은 언덕 고가 돌파
```

이후에도 바로 주문하지 않는다.
0B와 0D가 최종 확인을 맡는다.

## 실시간 게이트

최종 실시간 게이트:

```text
0B fresh tick
시장가 매수 체결 우위
60초 매수체결량 > 매도체결량
60초 매수비율 52% 이상
60초 매수거래대금 2천만 이상
0D fresh order book
매수잔량 비율 43% 이상
현재가가 최우선 매수호가 아래로 밀리지 않음
```

## 손절과 목표

이 전략은 `exit-first`를 통과해야만 주문 후보가 된다.

손절 후보:

```text
5분 기준봉 허리
최근 1분 눌림 저가
1분 MA60
```

세 값 중 진입가 아래에서 가장 가까운 값을 우선 사용한다.

기본 목표는 손절폭의 약 1.9배다.
손절폭이 너무 좁거나 넓으면 주문 후보로 넘기지 않는다.

## Progress 해석

0~70%는 매수 전 후보 추적이다.
70%는 매수 신호/매수 완료 구간이다.
70~100%는 보유 후 매도 추적 구간이다.

이 전략의 70% 전 단계는 다음 흐름을 따른다.

```text
today/D+ lane
time
change
trade-value
minute-data
10/15m context
5m base candle
5m base waist support
5m MA support
1m MA recovery
1m bullish turn
1m volume expansion
1m small-hill breakout
0B fresh
0B buy-flow
0D fresh
0D bid support
exit-first RR
```

## 주의

이 전략은 빠른 연습용이다.
작은 수익을 빠르게 확인하기 위한 구조이며,
큰 일봉 마디 전체를 먹는 전략이 아니다.

5분 기준봉은 작은 시공간의 기준봉이다.
KRX 정규장 일봉 기준봉과 같은 이름으로 섞지 않는다.
