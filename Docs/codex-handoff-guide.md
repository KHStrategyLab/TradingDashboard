// 이 파일은 UTF-8 기준으로 작성됨

# Codex 인수인계 업무지침서

작성일: 2026-06-04

이 문서는 새 Codex 창, 다른 PC, 또는 다른 개발자가 `TradingDashboard`를 이어받을 때 먼저 읽는 짧은 업무지침서다.
상세 원칙은 `AGENTS.md`와 `Docs/trading-dashboard-development-manual.md`를 따르되, 작업 시작 전에는 이 문서로 현재 상태와 금지선을 먼저 잡는다.

## 현재 역할 분리

이 프로젝트는 여러 Codex 창과 여러 PC에서 이어서 다룰 수 있다.
새 Codex는 먼저 자신이 어느 역할로 호출되었는지 확인해야 한다.

- 개발 Codex: 코드 수정, 빌드, 커밋, 푸시, 문서 반영을 담당한다.
- 실행테스트 Codex: 최신 코드를 내려받아 실행테스트, 장중 관찰, 오류 재현, 로그/스냅샷 수집을 담당한다.
- 현재 PC의 기본 역할은 실행테스트 Codex다.
- 실행테스트 Codex는 사용자가 명시적으로 요청하기 전까지 코드를 수정하지 않는다.
- 실행테스트 중 문제가 생기면 오류 메시지, 직전 로그 20~50줄, 화면 스샷, 관련 스냅샷 파일, 사용자가 누른 버튼과 시각을 정리해 개발 Codex에게 전달할 수 있게 만든다.

## 작업 전 확인 순서

1. 현재 브랜치와 작업트리 상태를 확인한다.
   - `git status --short`
   - `git branch --show-current`
2. 실행 중인 `TradingDashboard.exe`가 있는지 확인한다.
   - full `dotnet build .\TradingDashboard.csproj`는 프로젝트 설정상 실행 중인 앱을 종료할 수 있다.
   - 장중 실행 중이면 빌드 전에 사용자에게 확인한다.
3. `AGENTS.md`와 이 문서를 먼저 읽고, KRX/NXT/주문 안전 원칙을 다시 확인한다.
4. 수정은 작게 나누고, 기능이 연결된 영역을 넓게 건드리지 않는다.

## 절대 고정 원칙

### KRX/NXT

- KRX 전일종가는 화면 색상과 기준가의 최상위 기준이다.
- KRX 전일종가 기준가는 NXT/SOR/AL 값으로 덮어쓰지 않는다.
- NXT 시간대의 현재가, 호가, 체결, 분봉은 NXT 데이터로 본다.
- KRX fallback은 시장 미확정 임시값이지 NXT 데이터 대체값이 아니다.
- NXT 가능 종목은 KRX/NXT 분봉, 실시간 체결, 차트 캐시가 섞이면 안 된다.

### 주문 안전

- 실주문 연결은 마지막 단계다.
- 전략, Progress, 스냅샷, 알림, 모의 검증이 충분히 끝나기 전에는 실주문 실행부를 연결하지 않는다.
- Live Orders는 앱 시작 시 항상 OFF여야 한다.
- Engine Start와 Live Orders는 저장값으로 자동 ON 복원하지 않는다.
- NXT 주문은 KRX 정규장 주문과 같은 방식으로 단순 처리하지 않는다.

### 전략 철학

- 자동매매는 매수 버튼이 아니라 위험 필터다.
- 사야 할 자리를 찾기 전에, 사면 안 되는 자리를 먼저 제거한다.
- 진입은 예측이 아니라 구조적 손익비가 허락할 때만 한다.
- 보유 종목은 후보 종목보다 우선한다. 후보는 기회지만 보유는 이미 열린 위험이다.
- 매도는 생존이다. 익절은 분할, 손절은 빠르게 전량을 기본으로 한다.

## 현재 주요 기능 상태

- 조건검색식 1번 결과를 받아 기준봉 게이트를 통과한 종목만 왼쪽 목록에 표시한다.
- 장중 신규 편입 종목은 `NEW` 후보로 표시한다.
- 기준봉 게이트 통과 종목은 `D+` 배지로 표시한다.
- 보유 종목은 조건검색 목록과 별개로 최근조회, 0B 추적, 전략 분봉 준비 대상에 먼저 올린다.
- 일봉/주봉/월봉/분봉 차트와 호가/체결/종목정보/뉴스/공시 패널이 연결되어 있다.
- 전략 슬롯과 Progress Bar는 존재하지만 실주문 실행부로 확정하지 않는다.
- `Codex 확인` 스위치 ON 시 전략 장부 JSON, 차트 PNG, AI 검토 요청 JSON을 저장한다.
- 매도 신호 테스트는 현재 스냅샷 안에서 disabled 상태로 잠가두었다.

## 중요한 경로

### 코드와 문서

- 프로젝트 루트: 현재 PC의 실제 `TradingDashboard` Git 저장소 경로를 먼저 확인한다.
- 개발 지침: `AGENTS.md`
- 개발 매뉴얼: `Docs/trading-dashboard-development-manual.md`
- 함수 사전: `Docs/strategy-function-dictionary.md`
- 기준봉/워크플로우: `Docs/base-candle-definition-and-workflow.md`
- 예측 가능 매매 계약: `Docs/strategy-predictable-trade-contract.md`
- 매도/손절 계획: `Docs/Strategies/hybrid-exit-backtest-plan.md`

### 로컬 전용 데이터

아래 경로는 대체로 Git에 올리지 않는다. 개발 Codex와 실행테스트 Codex가 같은 상태를 봐야 한다면 Git이 아니라 외장하드, 원격 저장소, 또는 별도 파일 전달 방식으로 복사한다.

- `Storage/DebugSnapshots/`
- `Storage/AiReviewQueue/`
- `Storage/StrategyMinuteSeeds/`
- `Storage/StrategyAnchors/`
- `Storage/StrategyPositions/`
- `Storage/StrategyOrderJournal/`
- `Config/stock_master_cache.json`
- `Config/chart_candle_cache.json`
- `Config/watchlist_stock_cache.json`

## 스냅샷과 Codex 확인

- `Codex 확인` 스위치가 ON이면 3분마다 전체 전략 장부와 차트 스냅샷을 저장한다.
- 저장 위치:
  - `Storage/DebugSnapshots/*.json`
  - `Storage/DebugSnapshots/*.png`
  - `Storage/AiReviewQueue/pending/*.json`
- 이 기능은 자동매매 신호 실행이 아니라 복기와 검증 자료 수집용이다.
- 현재는 Codex가 스스로 깨어나 파일을 읽지는 않는다.
- 사용자가 “스냅샷 봐줘”라고 하면 Codex가 해당 파일을 읽고 분석한다.

## 실행테스트 Codex 운영 원칙

- 실행테스트 Codex는 `git pull`로 최신 코드를 받고 실행테스트를 돕는다.
- 실행테스트 Codex는 사용자가 명시적으로 요청하기 전까지 코드 수정, 커밋, 푸시를 하지 않는다.
- 실행테스트 Codex는 로그와 스냅샷을 읽고, 어디까지 실행됐는지와 무엇이 막혔는지를 설명한다.
- 실행 중 오류가 나면 다음을 챙긴다.
  - 오류 메시지 전체
  - 직전 로그 20~50줄
  - 화면 스샷
  - 해당 시각의 DebugSnapshots 파일
  - 어떤 종목, 몇 시, 어떤 버튼을 눌렀는지
- 실행테스트 환경에서 만든 로컬 캐시와 스냅샷은 Git 대상이 아니며, 필요하면 별도로 전달한다.

## Git 백업 규칙

- 사용자에게 “백업”은 `commit + push`를 뜻한다.
- 커밋 전에는 `git status --short`와 diff 범위를 확인한다.
- 커밋 메시지는 변경 목적을 짧게 적는다.
- 공개하면 안 되는 강의 원문, 스냅샷, 로컬 캐시, 계좌/토큰/설정 파일은 커밋하지 않는다.

## 새 Codex가 해야 할 첫 질문

새 창에서 바로 코드를 고치기 전에 다음을 확인한다.

- 지금 호출된 역할은 개발 Codex인가, 실행테스트 Codex인가?
- 현재 앱이 실행 중인가?
- 지금 사용자가 원하는 것은 분석인가, 수정인가, 백업인가?
- 실주문 관련 변경인가?
- KRX/NXT 데이터가 섞일 위험이 있는가?
- 스냅샷/캐시 파일은 Git 대상인가 로컬 데이터인가?

## 다음 작업 후보

우선순위는 장중 데이터 검증이 먼저다.

1. NXT/KRX 분봉과 0B가 시장별로 섞이지 않는지 실시간 확인한다.
2. 전략 분봉 장부가 봉 마감 때 시고저종, 이평선, 거래량을 제대로 고정하는지 확인한다.
3. Progress Bar가 전략별로 독립적으로 움직이는지 확인한다.
4. 스냅샷이 복기 가능한 차트 문맥을 충분히 담는지 확인한다.
5. 매도 신호 테스트는 현재 disabled 상태로 두고, 실시간 데이터 안정 후 다시 켠다.
6. 백테스트/리얼테스트용 매도 공식은 별도 문서와 별도 evaluator로만 다룬다.

## 새 창에서 가장 중요한 한 문장

데이터가 안 섞이고 제때 들어오는지 확인하기 전에는, 신호가 맞는지 논하지 않는다.
