이 파일은 UTF-8 기준으로 작성됨.

# AVGIF240 하단 회복 + 1분 실행 트리거 백테스트

작성일: 2026-06-06

## 목적

AVGIF 240 하단밴드 회복을 매수 후보 환경으로 보고, 실제 진입은 방금 완성된 1분봉의 구조 돌파로 한 번 더 확인한다.

핵심 의도는 다음과 같다.

- AVGIF 하단밴드 회복: 눌림 후 회복 후보
- 1분봉 MA5 위 종가: 짧은 흐름 회복
- 1분봉 직전고가 종가 돌파: 실제 매수 트리거
- 1분봉 거래량 필터: 거래가 붙는지 확인
- AVGIF 상단밴드 돌파: 익절성 매도
- 1분봉 직전저가 종가 훼손: 이탈/손절성 매도

실주문 흐름, Live Orders, RiskGuard는 수정하지 않았다. 이번 변경은 백테스트 후보 검증용이다.

## 적용 수식

```text
A = MA(C,240)

LowerBand =
    A
    + AVGIF(C - A, -1, 0.0)
    - 2 * STDEVIF(C - A, -1, 0.0)

UpperBand =
    A
    + AVGIF(C - A, 1, 0.0)
    + 2 * STDEVIF(C - A, 1, 0.0)

AvgifLowerRecoveryBuy = CROSSUP(C, LowerBand)
AvgifUpperExtensionSell = CROSSUP(C, UpperBand)

BuyTrigger_1m =
    b0.Close > priceMa5
    && b0.Close > b1.High

StopTrigger_1m =
    b0.Close < b1.Low
```

## 거래량 필터

세 단계로 비교했다.

```text
Light:
    b0.Volume >= vma5 * 0.7

Normal:
    b0.Volume >= vma5

Strong:
    b0.Volume >= vma5
    && vma5_0 > vma5_1
    && vma5_0 >= vma10_0
```

## 실행 명령

```powershell
dotnet run --no-build --project .\TradingDashboard.csproj -- --backtest-five-avgif240-1m-merge-sweep
```

## 결과

RunId:

```text
avgif240_1m_merge_sweep_20260606111910
```

| 필터 | 신호 | 거래 | 승률 | 평균수익 | 평균손실 | 기대값 | MFE | MAE | 연속손실 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Light | 53 | 7 | 28.57% | +1.029% | -0.691% | -0.200% | +1.477% | -0.972% | 4 |
| Normal | 53 | 7 | 57.14% | +1.765% | -0.641% | +0.734% | +2.756% | -1.049% | 1 |
| Strong | 53 | 7 | 28.57% | +0.216% | -1.052% | -0.689% | +1.395% | -1.317% | 3 |

## 해석

Normal 필터가 가장 좋았다.

현재 1분 거래량이 5봉 평균 이상이라는 조건이 지나치게 헐겁지도, 지나치게 늦지도 않은 중간 지점으로 보인다.

Light는 너무 많은 약한 돌파를 허용해 연속손실이 커졌고, Strong은 거래량 조건이 늦게 붙거나 과하게 걸러져 수익 기회를 줄인 것으로 보인다.

## 현재 판단

이 신호는 폐기 대상은 아니다.

다만 표본이 7거래로 작으므로 실전 후보로 바로 승격하지 않고, 다음 검증에서 아래 항목을 더 확인한다.

- Normal 필터 기준 손실 거래의 공통점
- AVGIF 하단 회복 당시 일봉/15분봉 위치
- 1분봉 직전고가 돌파 전후 거래량 이평선 방향
- 손절 전 먼저 +1% 이상 기회를 준 거래와 바로 실패한 거래의 차이
- 조건검색식 후보 루트와 결합했을 때 표본 증가 여부

## 결론

현재 기준으로는 `Normal`만 후속 검증 후보로 남긴다.

이 전략의 핵심은 AVGIF 하단 회복 자체가 아니라, 하단 회복 이후 1분봉에서 거래량이 유지된 상태로 직전고가를 종가 돌파하는지 여부다.
