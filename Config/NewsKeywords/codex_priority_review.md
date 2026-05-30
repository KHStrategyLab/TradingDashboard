# Codex News Keyword Priority Review

## 결론

이 사전은 네이버 검색어 확장용보다, 이미 가져온 뉴스의 제목/요약을 분석해서
태그와 중요도 점수를 붙이는 용도에 적합하다.

뉴스 검색 자체는 지금처럼 종목명/시장 핵심어 중심으로 작게 유지하고,
이 사전은 검색 결과를 걸러내고 정렬하는 후처리 엔진으로 사용하는 것이 안전하다.

## 1순위: 강한 재료

아래 묶음은 뉴스 중요도 점수를 크게 올린다.

- `orders_contracts`: 수주/계약
- `battery_orders_supply`: 이차전지/수주공급
- `performance_growth`: 실적/성장
- `shareholder_value`: 주가/주주환원
- `dividend_buyback`: 배당/자사주
- `market_supply`: 시장판세/수급
- `market_leadership`: 시장판세/주도주

## 2순위: 보조 재료

단독으로도 의미는 있지만, 1순위 단어와 같이 잡힐 때 더 중요하게 본다.

- `technology_products`: 기술/제품
- `investment_expansion`: 투자/확장
- `policy_government`: 정책/정부
- `analyst_view`: 증권가/전망
- `ai_collaboration`: AI 협력/기대
- `physical_ai_robotics`: 피지컬AI/로봇
- `dx_cloud_platform`: DX/클라우드/플랫폼
- `group_stock_strength`: 그룹주/동반강세
- 이차전지 중 `battery_ess_ai_power`, `battery_raw_materials`, `battery_technology_products`, `battery_policy_supply_chain`, `battery_earnings_recovery`

## 3순위: 약한 기대감

단독으로는 낮은 점수만 준다. 강한 재료와 같이 잡힐 때 보조 점수로 사용한다.

- `theme_momentum`: 테마/모멘텀
- `expectation_outlook`: 기대/전망
- `cooperation_bigtech`: 협력/회동/빅테크
- `visit_meeting_schedule`: 방한/회동/일정
- `market_outlook_expectation`: 시장판세/전망기대

## 위험 태그

긍정 단어가 잡히더라도 아래 묶음이 같이 잡히면 반드시 표시한다.

- `market_overheat_caution`: 시장판세/과열주의
- `general_negative`: 부정/주의

## 적용 방식 제안

- 제목 매칭은 요약 매칭보다 높은 점수를 준다.
- 긴 단어를 먼저 매칭한다.
- 같은 카테고리에서 여러 단어가 잡히면 점수를 누적하되 상한을 둔다.
- `positive`와 `negative`가 같이 잡히면 긍정만 보여주지 않는다.
- 처음에는 자동 매수 판단에 연결하지 않고, 뉴스 목록 태그와 정렬 보조로만 사용한다.

## 검색 적용 여부

바로 네이버 검색어로 모든 단어를 펼쳐 쓰면 안 된다.

이유:
- 키워드가 많아서 호출 횟수가 늘어난다.
- 검색어가 넓어져서 노이즈 뉴스가 늘어난다.
- 뉴스 엔진이 매매/키움 흐름보다 중요해지는 부작용이 생긴다.

따라서 1차 검색은 종목명 또는 제한된 시장 검색어로 하고,
2차로 이 사전을 사용해서 뉴스별 점수와 태그를 계산한다.

