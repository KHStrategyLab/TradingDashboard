# 2026-06-06 1분봉 하이켄아시 매도신호 백테스트

이 파일은 UTF-8 기준으로 작성됨.

## 목적

AVGIF240 하단밴드 회복 후 1분봉 MA5 위 직전고가 돌파 매수 후보에 대해, 1분봉 하이켄아시 기반 매도식을 비교한다.

이번 테스트는 실주문과 무관한 백테스트 전용 비교다.

## 고정 매수 조건

- Entry: AVGIF240 하단밴드 회복
- Trigger: 1분봉 종가가 MA5 위에서 직전 1분봉 고가 돌파
- Volume: Normal 조건, 즉 현재 1분봉 거래량 >= 1분봉 거래량 5이평

## 비교 매도 조건

```text
HA_Close0 = (Open0 + High0 + Low0 + Close0) / 4
HA_Open0 = (HA_Open1 + HA_Close1) / 2

HA_BearTurn = HA_Bull1 AND HA_Bear0
HA_BearHold2 = HA_Bear0 AND HA_Bear1

PrevLowDamage = Close0 < Low1
Ma5Damage = Close0 < Ma5_1m
VolumeWeak = Close0 < Close1 AND Volume0 < Vma5
```

테스트 항목:

- `HA_PrevLowDamage`
- `HA_BearTurn`
- `HA_BearTurnAndPrevLowDamage`
- `HA_BearHold2`
- `HA_PrevLowDamageOrBearHold2`
- `HA_BearTurnAndMa5Damage`
- `HA_BearTurnAndVolumeWeak`

반대 방향 확인을 위해 결과 reason에는 `inverseBullTurn`, `inverseBullHold2`도 기록한다.
이는 매수 신호가 아니라 “매도하면 안 되는 회복 신호가 같이 있었는가”를 나중에 확인하기 위한 보조 태그다.

## 실행 명령

```powershell
dotnet run --no-build --project .\TradingDashboard.csproj -- --backtest-five-avgif240-1m-ha-exit-sweep
```

## 실행 결과

RunId: `avgif240_1m_ha_exit_sweep_20260606113717`

| ExitMode | Trades | WinRate | AvgProfit | AvgLoss | Expectancy | MFE | MAE | AvgHold |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| HA_PrevLowDamage | 7 | 57.14% | 1.76% | -0.64% | 0.73% | 2.76% | -1.05% | 13.29m |
| HA_BearTurn | 7 | 71.43% | 1.50% | -0.15% | 1.03% | 3.06% | -1.04% | 7.00m |
| HA_BearTurnAndPrevLowDamage | 7 | 57.14% | 1.60% | -0.21% | 0.82% | 3.24% | -1.06% | 21.86m |
| HA_BearHold2 | 7 | 85.71% | 1.33% | -1.25% | 0.96% | 3.21% | -1.04% | 9.00m |
| HA_PrevLowDamageOrBearHold2 | 7 | 71.43% | 1.23% | -0.96% | 0.61% | 2.58% | -1.04% | 6.57m |
| HA_BearTurnAndMa5Damage | 7 | 71.43% | 1.50% | -0.15% | 1.03% | 3.06% | -1.06% | 11.86m |
| HA_BearTurnAndVolumeWeak | 7 | 100.00% | 1.56% | 0.00% | 1.56% | 3.56% | -1.04% | 16.00m |

## 1차 해석

현재 표본에서는 `HA_BearTurnAndVolumeWeak`가 가장 좋다.
다만 거래 수가 7건뿐이므로 확정 신호가 아니라 후속 검증 후보로 둔다.

`HA_BearTurn`과 `HA_BearTurnAndMa5Damage`도 기대값이 높고 손실폭이 작다.
단순 직전저가 훼손보다 하이켄아시 음전 전환을 같이 보는 쪽이 더 나아 보인다.

`HA_BearHold2`는 승률은 높지만 한 번의 손실폭이 커서 전량 손절 기준으로는 조심해야 한다.

## 다음 확인 후보

- 같은 매수 조건에서 종목 수를 늘린 뒤 재검증
- `HA_BearTurnAndVolumeWeak`가 늦게 팔아서 수익을 키운 것인지, 우연히 손실을 피한 것인지 trade CSV로 복기
- `inverseBullTurn`, `inverseBullHold2`가 매도 보류 태그로 쓸 만한지 실패/성공 사례 확인
- 이 결과를 실프로그램 매도 로직에 직접 연결하지 말고, 백테스트/리얼테스트 검증 자료로만 유지
