# TradingDashboard Function Map

Date: 2026-05-31

이 문서는 함수 전체 목록이 아니라 유지보수용 역할 사전이다.
새 기능을 붙일 때 먼저 이 문서를 보고 함수의 소유 데이터, 호출 시점, 금지 기준을 확인한다.

## 읽는 기준

- 화면 표시용 값과 전략 판단용 값을 섞지 않는다.
- KRX/NXT/SOR 기준은 `Docs/AGENTS.md`와 `Docs/trading-dashboard-development-manual.md`를 우선한다.
- 키움 TR 필드가 애매하면 `Docs/KiwoomReferences/*`와 원본 `Docs/키움 REST API 문서.xlsx`를 확인한다.
- 경량엔진 이식 전에는 KHStrategyLab의 `KHStrategyLab_Architecture_Guide.md`와 Archive의 경량엔진 문서를 같이 본다.

## MainWindow 시작/상태

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `MainWindow()` | `MainWindow.xaml.cs` | 서비스 생성, 컬렉션 바인딩, 호가/잔고/전략 초기 UI 연결 | 초기화 순서를 바꾸면 XAML 컨트롤 null 또는 ItemsSource 누락이 생길 수 있다. |
| `MainWindow_Loaded` | `MainWindow.xaml.cs` | 시작 오버레이, 장상태 prime, 조건검색 watchlist 로드, 실시간 시작 | REST disabled 경로와 캐시 fallback을 유지한다. |
| `PrimeMarketStatusBeforeWatchlistAsync` | `MainWindow.xaml.cs` | watchlist 로드 전에 0s 장상태를 먼저 확인 | KRX/NXT 모드 선택에 영향. 실패 시 앱 시작을 막지 않는다. |
| `MarkMarketStatusUnknown` | `MainWindow.xaml.cs` | 장상태를 임시 unknown으로 표시 | unknown 상태를 KRX 확정으로 해석하지 않는다. |
| `MainWindow_Closed` | `MainWindow.xaml.cs` | WebSocket/CTS/잔고 요청 정리 | 종료 중 재연결 또는 늦은 응답 반영을 막는 정리 지점. |

## Watchlist / 조건검색 / 검색

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `LoadWatchListFromKiwoomConditionAsync` | `MainWindow.xaml.cs` | 조건검색 초기 목록 로드 | 실패 시 캐시 fallback 경로를 유지한다. |
| `ApplyCachedWatchListFallback` | `MainWindow.xaml.cs` | 조건검색 실패 시 watchlist 캐시로 복원 | 캐시는 화면 복원용이며 새 시장 확정으로 과신하지 않는다. |
| `ApplyWatchList` | `MainWindow.xaml.cs` | watchlist 컬렉션과 code map 구성 | Clear/Add는 선택 상태와 실시간 등록에 영향을 준다. |
| `LoadWatchlistCache` | `MainWindow.xaml.cs` | `Config/watchlist_stock_cache.json` 로드 | Git 백업 대상이 아닌 로컬 실행 캐시다. |
| `UpsertWatchlistMemoryCache` | `MainWindow.xaml.cs` | 종목 기본정보, 가격, KRX 기준가 캐시 갱신 | KRX 전일종가 신뢰 날짜를 함부로 덮지 않는다. |
| `SearchStockMasterItemsAsync` | `KiwoomRestConditionService.cs` | 종목 마스터 기반 자동완성 후보 조회 | ETF/ETN/SPAC 제외 정책을 유지한다. |
| `OpenSelectedStockSuggestionAsync` | `MainWindow.xaml.cs` | 자동완성 선택 종목 열기 | 선택 후 autocomplete 재팝업 방지와 최근목록 focus landing 유지. |

## 종목 선택 흐름

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `WatchListBox_SelectionChanged` | `MainWindow.xaml.cs` | watchlist 선택 이벤트 진입점 | 중복 선택 방어와 cancellation 흐름을 거친다. |
| `LoadNewsForSelectedStockAsync` | `MainWindow.xaml.cs` | 선택 종목의 차트, 기준가, 호가, 뉴스, 공시, 상태 조회 시작 | 차트를 먼저 시작하고 기준가 -> 호가 -> 종목정보 순서로 간다. `_selectionVersion`과 `_selectedRequestCts`가 늦은 응답 차단의 핵심이다. |
| `IsCurrentSelection` | `MainWindow.xaml.cs` | 늦게 돌아온 응답이 현재 선택인지 확인 | 모든 비동기 화면 반영 전에 확인해야 한다. |
| `DisposeCanceledRequestLater` | `MainWindow.xaml.cs` | 취소된 CTS를 늦게 dispose | 빠른 선택 전환 중 `ObjectDisposedException` 방어. |
| `ClearSelectedChartVisuals` | `MainWindow.xaml.cs` | 선택 변경 시 차트/드래그 상태 초기화 | 이전 종목의 시각 요소가 남지 않게 한다. |

## KRX/NXT 기준가와 종목 상태

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `LoadSelectedBasePriceAsync` | `MainWindow.xaml.cs` | 선택 종목의 KRX 전일종가 기준가 잠금 | 기준가는 NXT 현재가, 종가, 차트 값으로 덮으면 안 된다. |
| `GetKrxPreviousClosePriceAsync` | `KiwoomRestConditionService.cs` | KRX 전일종가 조회 | 색상/등락률 기준 소유자다. |
| `LoadSelectedStockStatusAsync` | `MainWindow.xaml.cs` | 선택 종목 현재가/시고저/거래량/거래대금/프로그램 조회 | NXT 표시 중 KRX OHLC fallback 금지. KRX에서 가져와도 되는 값은 전일종가뿐이다. |
| `GetStockStatusMetricsByGuideAsync` | `KiwoomRestConditionService.cs` | MTS 기준에 맞춘 종목 상태 조회 | `_NX`, `_AL` 사용 정책과 계산 보정이 들어 있다. |
| `LoadExecutionSummaryByMarketAsync` | `MainWindow.xaml.cs` | 매수/매도 체결량, 프로그램, 일별거래상세 보조 조회 | NXT/SOR 값이 비면 KRX 체결값으로 섞지 않는다. |
| `ShouldUseFinalDailyVolumeForRatio` | `MainWindow.xaml.cs` | 전일동시/거래량을 `_AL` 통합값으로 보정할지 결정 | NXT 장중/고정 시간대 거래량 덮어쓰기 방지와 연결된다. |
| `ApplySelectedPriceInfoColors` | `MainWindow.xaml.cs` | 화면 가격 색상 적용 | 항상 KRX 전일종가 기준 색상. |

## 호가 / 실시간

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `StartRealtimeTradeAsync` | `MainWindow.Realtime.cs` | 실시간 WebSocket 시작 | 재연결/종료 CTS 흐름과 연결된다. |
| `RegisterRealtime0BAsync` | `MainWindow.Realtime.cs` | 관심종목 0B 주식체결 등록 | 경량엔진 이식 시 현재봉 갱신 입력 후보. |
| `RegisterSelectedRealtime0DAsync` | `MainWindow.Realtime.cs` | 선택 종목 0D/0H 호가 등록 | 선택 종목 전용. 전체 후보 엔진으로 확대하면 별도 분리 필요. |
| `ApplyRealtimePayload` | `MainWindow.Realtime.cs` | WebSocket REAL payload 분기 | type별 처리 순서를 바꾸면 호가/조건/0B 반영이 꼬일 수 있다. |
| `ApplyRealtimeItem` | `MainWindow.Realtime.cs` | 0B 체결 실시간을 watchlist/선택 화면/차트에 반영 | 화면 표시용 0B와 전략 판단용 경량엔진 입력을 분리해야 한다. |
| `ApplyRealtimeHogaItem` | `MainWindow.Realtime.cs` | 0D 호가 rows 반영 | API 1호가는 화면 첫 줄이 아니라 현재가에 가까운 호가다. 매도호가 표시 순서 주의. |
| `ApplyHogaRows` | `MainWindow.Realtime.cs` | 매도/매수 10호가 UI 컬렉션 갱신 | 빈 응답으로 기존 유효 호가를 지우지 않는다. |
| `HighlightCenterPriceInHoga` | `MainWindow.Realtime.cs` | 현재가와 같은 호가 칸 강조 | KRX 전일종가 색상 기준과 현재가 강조는 서로 다른 역할이다. |
| `UpdateHogaRateMarkers` | `MainWindow.Realtime.cs` | 각 호가의 기준가 대비 등락률 표시 | 기준가는 `_krxPrevClosePrice`. |

## 차트

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `StartSelectedChartRender` | `MainWindow.Chart.cs` | 선택 종목 차트 렌더링 시작 | 화면 차트용이며 전략 판단 seed와 직접 결합하지 않는다. |
| `RenderSelectedChartAsync` | `MainWindow.Chart.cs` | 일/주/월/분봉 REST 조회와 캐시 적용 | 메모리 캐시 -> 일/주/月 파일 캐시 -> REST 순서. selectionVersion/chartVersion으로 늦은 응답 방어. |
| `StartInitialChartFileCachePreload` | `MainWindow.Chart.cs` | 초기 조건식 통과 종목의 일/주/月 파일 캐시 프리로드 | 백그라운드 1회 보조 작업. 분봉은 제외한다. |
| `ChartCandleCacheStore` | `ChartCandleCacheStore.cs` | `Config/chart_candle_cache.json` 저장/로드 | KRX/NXT/기간 키를 분리한다. 파일은 로컬 캐시라 git에 올리지 않는다. |
| `GetMinuteCandlesAsync` | `KiwoomRestConditionService.cs` | ka10080 분봉 조회 | KRX=6자리, NXT=`_NX`. NXT 실패 시 KRX fallback 금지. |
| `ApplyChartCandles` | `MainWindow.Chart.cs` | 조회된 봉을 현재 차트 상태에 적용 | `_currentChartCandles`는 화면 차트 상태다. 경량엔진 캐시와 분리할 것. |
| `ApplyRealtimeChartTick` | `MainWindow.Chart.cs` | 0B 틱으로 현재 화면 차트 진행봉 갱신 | 선택된 단일 종목 화면 표시용. 전략 판단으로 역류 금지. |
| `ApplyRealtimeMinuteChartTick` | `MainWindow.Chart.cs` | 분봉 현재 버킷 갱신 | 10분 정각 확정봉 교체 구조와 경량엔진의 NumericCandleEngine 후보. |
| `DrawPriceChart` / `DrawVolumeChart` | `MainWindow.Chart.cs` | 차트 렌더링 | 실전 전략 엔진은 차트를 그리지 않고 숫자 캐시만 갱신해야 한다. |

## 뉴스 / 공시 / 알림

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `LoadNewsAsync` | `MainWindow.xaml.cs` | 선택 종목 뉴스 로드 | 선택 버전이 바뀌면 반영하지 않는다. |
| `LoadMarketNewsAsync` | `MainWindow.xaml.cs` | News 탭 시장 뉴스/검색 조회 | Naver API 제한은 키움 시세 흐름과 분리한다. |
| `LoadMarketNewsThumbnailsAsync` | `MainWindow.xaml.cs` | 기사 og:image 지연 로드 | 썸네일 실패는 뉴스 표시 실패가 아니다. |
| `TrySendLateNewsAlertAsync` | `MainWindow.LateNews.cs` | 조건 편입 종목 뉴스 알림 | 매수 신호가 아니라 재료 확인 보조. 키워드 사전이 없으면 알림은 skip한다. |
| `NewsKeywordFilterService.Rank` | `NewsKeywordFilterService.cs` | 뉴스 제목/요약 키워드 점수화 | 알림 통과 점수는 positive 중심. negative/caution은 태그/소폭 감점으로만 반영한다. 사전 실패 시 원본 뉴스를 통과시키지 않는다. |
| `LoadDisclosuresAsync` | `MainWindow.xaml.cs` | 선택 종목 공시 조회 | 전체 시장 훑기가 아니라 현재 후보/선택 종목 중심. |
| `DartDisclosureAlertService.TrySendRecentDisclosureAlertAsync` | `DartDisclosureAlertService.cs` | 조건 편입 공시 알림 | 좋은 재료 공시만 우선 통과. 위험 공시는 별도 태그 후보. |

## 잔고 / 손익 / 주문 클라이언트

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `RefreshBalanceAsync` | `MainWindow.Balance.cs` | kt00018 기준 잔고 화면 갱신 | 현재는 KRX 조회가 기본. MTS/SOR 일치 검증 후 확장한다. |
| `RefreshRealizedProfitAsync` | `MainWindow.Balance.cs` | ka10074 오늘 실현손익 갱신 | 수수료/세금과 함께 표시한다. |
| `VerifyBalanceAgainstMtsAsync` | `MainWindow.Balance.cs` | 숨은 검증: kt00018 KRX/NXT + kt00005 KRX 비교 로그 | 운영 UI가 아니라 검증용. |
| `KiwoomTradingClient.BuyAsync` | `KiwoomTradingClient.cs` | kt10000 매수 주문 전송 | 아직 전략 실행과 직접 연결하지 않는다. RiskGuard 이후에만 사용. |
| `KiwoomTradingClient.SellAsync` | `KiwoomTradingClient.cs` | kt10001 매도 주문 전송 | 자동매도 연결 전 포지션/수량 검증 필요. |
| `GetOpenOrdersAsync` | `KiwoomTradingClient.cs` | ka10075 미체결 조회 | SOR 추적은 integrated exchange query mode를 사용한다. |
| `GetFillsAsync` | `KiwoomTradingClient.cs` | ka10076 체결 조회 | `sor_yn`, `stex_tp_txt`는 SOR 확인 기준이다. |
| `GetEvaluationBalanceAsync` | `KiwoomTradingClient.cs` | kt00018 계좌평가잔고 | 잔고 평가는 주문 경로 증명이 아니다. |
| `WaitRestSlotAsync` | `KiwoomTradingClient.cs` | 주문/계좌 TR 1초 5회 제한 방어 | 키움 시세 TR 제한과 별도 gate다. |

## 전략 슬롯 / Progress

| 함수 | 파일 | 역할 | 주의점 |
|---|---|---|---|
| `InitializeStrategySlots` | `MainWindow.StrategySlots.cs` | 전략 슬롯 UI/Progress rows 초기화 | Progress 표시 연결만 담당한다. |
| `GetStrategySlotSettings` | `MainWindow.StrategySlots.cs` | UI 토글에서 ON/OFF 전략 목록 생성 | OFF 전략은 평가 전에 제외한다. |
| `EvaluateEnabledStrategySlots` | `MainWindow.StrategySlots.cs` | 켜진 전략 슬롯만 평가 | 주문 여부 판단 금지. 전략 결과만 반환한다. |
| `UpdateStrategyControlBoard` | `MainWindow.StrategySlots.cs` | Engine/Live Orders/예산/슬롯/중복 정책 표시 | 전광판은 실행 상태 표시다. 주문 실행 자체가 아니다. |
| `TryRejectEngineLockedStrategyChange` | `MainWindow.StrategySlots.cs` | Engine Start 중 전략 설정 변경 차단 | 실행 중 전략 슬롯/중복 정책 변경을 되돌린다. |
| `UpdateStrategyProgressRows` | `MainWindow.StrategySlots.cs` | 선택 종목의 전략 평가 결과를 Progress 탭에 표시 | `StrategyProgressSnapshot`의 0~70/70~100 표준 진행률을 UI에 표시한다. |
| `StrategySlotRegistry.EvaluateEnabled` | `StrategySlotRegistry.cs` | registry 기준 전략 평가 호출 | 새 전략은 registry 등록과 descriptor 문서 경로가 같이 필요하다. |
| `StrategyEvaluationResult.Waiting` | `StrategyEvaluationResult.cs` | 전략 미구현/대기 상태 결과 생성 | 대기 상태를 매수 후보로 해석하면 안 된다. |
| `StrategyProgressSnapshot.Empty` | `StrategyProgressSnapshot.cs` | Progress 기본값 | 0% WAIT. 실제 단계 계산 전 표시용. |
| `StrategyProgressCalculator.Build` | `StrategyProgressCalculator.cs` | 전략별 단계 수를 공통 진행률로 변환 | 매수 전 단계는 0~70%, 보유 후 매도 단계는 70~100% 안에서 자동 분배한다. |

## 경량엔진 이식 후보

현재 TradingDashboard에는 화면 차트와 전략 슬롯 뼈대가 있으나, KHStrategyLab의 경량엔진처럼 후보별 숫자 장부가 아직 분리되어 있지 않다.
이식할 때는 새 엔진을 화면 차트 함수에 직접 붙이지 않는다.

| 필요 역할 | 현재 참고 함수/파일 | KHStrategyLab 참고 | 기준 |
|---|---|---|---|
| 분봉 seed 조회 | `GetMinuteCandlesAsync` | `MainWindow.MinuteCache.cs` | KRX=6자리, NXT=`_NX`, fallback 금지 |
| 후보별 캐시 키 | 새 모델 필요 | `CandidateMinuteCache`, `BuildMinuteCacheKey` | `종목코드|KRX`, `종목코드|NXT` |
| 현재봉 갱신 입력 | `ApplyRealtimeItem`, `ApplyRealtimeMinuteChartTick` | `ApplyRealtimeTickToCandidateMinuteCache` | 화면 차트와 전략 캐시 갱신을 분리 |
| READY 판정 | 새 서비스 필요 | `TryGetReadyCandidateMinuteCache` | 10분봉/5분봉 최소 개수 충족 전 매수 판단 차단 |
| 상태머신 | 전략 슬롯별 `Evaluate` 확장 | `MainWindow.Strategy.BuySignalCheck.cs` | WAIT -> pullback -> recovery -> signal 흐름 |
| 주문 연결 | `KiwoomTradingClient` | `EvaluateLiveBuyRiskGuard` | 신호 -> RiskGuard -> 주문 순서. 신호가 주문을 직접 보내지 않는다. |

## 다음에 이 문서를 확장할 때

- 새 함수가 화면 표시용인지 전략 판단용인지 먼저 적는다.
- 소유 데이터가 `_currentChartCandles`, `_balanceHoldings`, `_watchlistMemoryCache`, 향후 `CandidateMinuteCache` 중 무엇인지 적는다.
- KRX/NXT 기준을 바꾸는 함수는 반드시 주의점을 남긴다.
- 주문 관련 함수는 RiskGuard 이전/이후 위치를 명확히 적는다.
