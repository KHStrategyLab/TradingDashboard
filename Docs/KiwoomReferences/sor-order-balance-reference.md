# Kiwoom SOR Order / Balance Reference Extract

Source: `Docs/?? REST API ??.xlsx`

Purpose: fast local reference for SOR ON buy/sell/balance verification before porting order logic.

## Extracted TRs

- `kt10000`: 주식 매수주문(kt10000)
- `kt10001`: 주식 매도주문(kt10001)
- `kt10002`: 주식 정정주문(kt10002)
- `kt10003`: 주식 취소주문(kt10003)
- `kt00005`: 체결잔고요청(kt00005)
- `kt00009`: 계좌별주문체결현황요청(kt00009)
- `kt00018`: 계좌평가잔고내역요청(kt00018)
- `ka10075`: 미체결요청(ka10075)
- `ka10076`: 체결요청(ka10076)
- `ka10100`: 종목정보 조회(ka10100)
- `00`: 주문체결(00)
- `04`: 잔고(04)

## kt10000 - 주식 매수주문(kt10000)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 주문 > 주식 매수주문(kt10000) |  |  |  |  |
| API 명 |  | 주식 매수주문 |  |  |  |  |
| API ID |  | kt10000 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/ordr |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | dmst_stex_tp | 국내거래소구분 | String | Y | 3 | KRX,NXT,SOR |
|  | stk_cd | 종목코드 | String | Y | 12 |  |
|  | ord_qty | 주문수량 | String | Y | 12 |  |
|  | ord_uv | 주문단가 | String | N | 12 |  |
|  | trde_tp | 매매구분 | String | Y | 2 | 0:보통 , 3:시장가 , 5:조건부지정가 , 81:장마감후시간외 , 61:장시작전시간외, 62:시간외단일가 , 6:최유리지정가 , 7:최우선지정가 , 10:보통(IOC) , 13:시장가(IOC) , 16:최유리(IOC) , 20:보통(FOK) , 23:시장가(FOK) , 26:최유리(FOK) , 28:스톱지정가,29:중간가,30:중간가(IOC),31:중간가(FOK) |
|  | cond_uv | 조건단가 | String | N | 12 |  |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | ord_no | 주문번호 | String | N | 7 |  |
|  | dmst_stex_tp | 국내거래소구분 | String | N | 6 |  |
| Request Example |  |  |  |  |  |  |
| {<br>    "dmst_stex_tp": "KRX",<br>    "stk_cd": "005930",<br>    "ord_qty": "1",<br>    "ord_uv": "",<br>    "trde_tp": "3",<br>    "cond_uv": ""<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "ord_no" : "00024"<br>    "return_code":0,<br>    "return_msg":"정상적으로 처리되었습니다"<br>} |  |  |  |  |  |  |

## kt10001 - 주식 매도주문(kt10001)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 주문 > 주식 매도주문(kt10001) |  |  |  |  |
| API 명 |  | 주식 매도주문 |  |  |  |  |
| API ID |  | kt10001 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/ordr |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | dmst_stex_tp | 국내거래소구분 | String | Y | 3 | KRX,NXT,SOR |
|  | stk_cd | 종목코드 | String | Y | 12 |  |
|  | ord_qty | 주문수량 | String | Y | 12 |  |
|  | ord_uv | 주문단가 | String | N | 12 |  |
|  | trde_tp | 매매구분 | String | Y | 2 | 0:보통 , 3:시장가 , 5:조건부지정가 , 81:장마감후시간외 , 61:장시작전시간외, 62:시간외단일가 , 6:최유리지정가 , 7:최우선지정가 , 10:보통(IOC) , 13:시장가(IOC) , 16:최유리(IOC) , 20:보통(FOK) , 23:시장가(FOK) , 26:최유리(FOK) , 28:스톱지정가,29:중간가,30:중간가(IOC),31:중간가(FOK) |
|  | cond_uv | 조건단가 | String | N | 12 |  |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | ord_no | 주문번호 | String | N | 7 |  |
|  | dmst_stex_tp | 국내거래소구분 | String | N | 6 |  |
| Request Example |  |  |  |  |  |  |
| {<br>    "dmst_stex_tp": "KRX",<br>    "stk_cd": "005930",<br>    "ord_qty": "1",<br>    "ord_uv": "",<br>    "trde_tp": "3",<br>    "cond_uv": ""<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "ord_no": "0000138",<br>    "dmst_stex_tp": "KRX",<br>    "return_code": 0,<br>    "return_msg": "매도주문이 완료되었습니다."<br>} |  |  |  |  |  |  |

## kt10002 - 주식 정정주문(kt10002)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 주문 > 주식 정정주문(kt10002) |  |  |  |  |
| API 명 |  | 주식 정정주문 |  |  |  |  |
| API ID |  | kt10002 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/ordr |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | dmst_stex_tp | 국내거래소구분 | String | Y | 3 | KRX,NXT,SOR |
|  | orig_ord_no | 원주문번호 | String | Y | 7 |  |
|  | stk_cd | 종목코드 | String | Y | 12 |  |
|  | mdfy_qty | 정정수량 | String | Y | 12 |  |
|  | mdfy_uv | 정정단가 | String | Y | 12 |  |
|  | mdfy_cond_uv | 정정조건단가 | String | N | 12 |  |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | ord_no | 주문번호 | String | N | 7 |  |
|  | base_orig_ord_no | 모주문번호 | String | N | 7 |  |
|  | mdfy_qty | 정정수량 | String | N | 12 |  |
|  | dmst_stex_tp | 국내거래소구분 | String | N | 6 |  |
| Request Example |  |  |  |  |  |  |
| {<br>    "dmst_stex_tp": "KRX",<br>    "orig_ord_no": "0000139",<br>    "stk_cd": "005930",<br>    "mdfy_qty": "1",<br>    "mdfy_uv": "199700",<br>    "mdfy_cond_uv": ""<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "ord_no": "0000140",<br>    "base_orig_ord_no": "0000139",<br>    "mdfy_qty": "000000000001",<br>    "dmst_stex_tp": "KRX",<br>    "return_code": 0,<br>    "return_msg": "매수정정 주문입력이 완료되었습니다"<br>} |  |  |  |  |  |  |

## kt10003 - 주식 취소주문(kt10003)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 주문 > 주식 취소주문(kt10003) |  |  |  |  |
| API 명 |  | 주식 취소주문 |  |  |  |  |
| API ID |  | kt10003 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/ordr |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | dmst_stex_tp | 국내거래소구분 | String | Y | 3 | KRX,NXT,SOR |
|  | orig_ord_no | 원주문번호 | String | Y | 7 |  |
|  | stk_cd | 종목코드 | String | Y | 12 |  |
|  | cncl_qty | 취소수량 | String | Y | 12 | '0' 입력시 잔량 전부 취소 |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | ord_no | 주문번호 | String | N | 7 |  |
|  | base_orig_ord_no | 모주문번호 | String | N | 7 |  |
|  | cncl_qty | 취소수량 | String | N | 12 |  |
| Request Example |  |  |  |  |  |  |
| {<br>    "dmst_stex_tp": "KRX",<br>    "orig_ord_no": "0000140",<br>    "stk_cd": "005930",<br>    "cncl_qty": "1"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "ord_no": "0000141",<br>    "base_orig_ord_no": "0000139",<br>    "cncl_qty": "000000000001",<br>    "return_code": 0,<br>    "return_msg": "매수취소 주문입력이 완료되었습니다"<br>} |  |  |  |  |  |  |

## kt00005 - 체결잔고요청(kt00005)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 계좌 > 체결잔고요청(kt00005) |  |  |  |  |
| API 명 |  | 체결잔고요청 |  |  |  |  |
| API ID |  | kt00005 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/acnt |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 체결 잔고 정보를 조회합니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | dmst_stex_tp | 국내거래소구분 | String | Y | 6 | KRX:한국거래소,NXT:넥스트트레이드 |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | entr | 예수금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | entr_d1 | 예수금D+1 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | entr_d2 | 예수금D+2 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | pymn_alow_amt | 출금가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | uncl_stk_amt | 미수확보금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | repl_amt | 대용금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | rght_repl_amt | 권리대용금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | ord_alowa | 주문가능현금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | ch_uncla | 현금미수금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | crd_int_npay_gold | 신용이자미납금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | etc_loana | 기타대여금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | nrpy_loan | 미상환융자금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | profa_ch | 증거금현금 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | repl_profa | 증거금대용 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | stk_buy_tot_amt | 주식매수총액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | evlt_amt_tot | 평가금액합계 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | tot_pl_tot | 총손익합계 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | tot_pl_rt | 총손익률 | String | N | 12 | 단위: %, 소수점 넷째 자리까지 포맷된 백분율 |
|  | tot_re_buy_alowa | 총재매수가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | 20ord_alow_amt | 20%주문가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | 30ord_alow_amt | 30%주문가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | 40ord_alow_amt | 40%주문가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | 50ord_alow_amt | 50%주문가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | 60ord_alow_amt | 60%주문가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | 100ord_alow_amt | 100%주문가능금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | crd_loan_tot | 신용융자합계 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | crd_loan_ls_tot | 신용융자대주합계 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | crd_grnt_rt | 신용담보비율 | String | N | 12 | 단위: %, 소수점 둘째 자리까지 포맷된 백분율 |
|  | dpst_grnt_use_amt_amt | 예탁담보대출금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | grnt_loan_amt | 매도담보대출금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | stk_cntr_remn | 종목별체결잔고 | LIST | N |  |  |
|  | - crd_tp | 신용구분 | String | N | 2 |  |
|  | - loan_dt | 대출일 | String | N | 8 | YYYMMDD |
|  | - expr_dt | 만기일 | String | N | 8 | YYYMMDD |
|  | - stk_cd | 종목번호 | String | N | 12 | 접두어 1자리 + 종목코드 6자리, 접두어(A: 주식 / J: ELW / Q: ETN) |
|  | - stk_nm | 종목명 | String | N | 30 |  |
|  | - setl_remn | 결제잔고 | String | N | 12 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - cur_qty | 현재잔고 | String | N | 12 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - cur_prc | 현재가 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - buy_uv | 매입단가 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - pur_amt | 매입금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - evlt_amt | 평가금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - evltv_prft | 평가손익 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - pl_rt | 손익률 | String | N | 12 | 단위: %, 소수점 넷째 자리까지 포맷된 백분율 |
| Request Example |  |  |  |  |  |  |
| {<br>    "dmst_stex_tp": "KRX"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "entr": "000000017534",<br>    "entr_d1": "000000017450",<br>    "entr_d2": "000000012550",<br>    "pymn_alow_amt": "000000085341",<br>    "uncl_stk_amt": "000000000000",<br>    "repl_amt": "000003915500",<br>    "rght_repl_amt": "000000000000",<br>    "ord_alowa": "000000085341",<br>    "ch_uncla": "000000000000",<br>    "crd_int_npay_gold": "000000000000",<br>    "etc_loana": "000000000000",<br>    "nrpy_loan": "000000000000",<br>    "profa_ch": "000000032193",<br>    "repl_profa": "000000000000",<br>    "stk_buy_tot_amt": "000006122786",<br>    "evlt_amt_tot": "000006236342",<br>    "tot_pl_tot": "000000113556",<br>    "tot_pl_rt": "1.8546",<br>    "tot_re_buy_alowa": "000000135970",<br>    "20ord_alow_amt": "000000012550",<br>    "30ord_alow_amt": "000000012550",<br>    "40ord_alow_amt": "000000012550",<br>    "50ord_alow_amt": "000000012550",<br>    "60ord_alow_amt": "000000012550",<br>    "100ord_alow_amt": "000000012550",<br>    "crd_loan_tot": "000000000000",<br>    "crd_loan_ls_tot": "000000000000",<br>    "crd_grnt_rt": "0.00",<br>    "dpst_grnt_use_amt_amt": "000000000000",<br>    "grnt_loan_amt": "000000000000",<br>    "stk_cntr_remn": [<br>        {<br>            "crd_tp": "00",<br>            "loan_dt": "",<br>            "expr_dt": "",<br>            "stk_cd": "A005930",<br>            "stk_nm": "삼성전자",<br>            "setl_remn": "000000000003",<br>            "cur_qty": "000000000003",<br>            "cur_prc": "000000070000",<br>            "buy_uv": "000000124500",<br>            "pur_amt": "000000373500",<br>            "evlt_amt": "000000209542",<br>            "evltv_prft": "-00000163958",<br>            "pl_rt": "-43.8977"<br>        }<br>    ],<br>    "return_code": 0,<br>    "return_msg": "조회가 완료되었습니다."<br>} |  |  |  |  |  |  |

## kt00009 - 계좌별주문체결현황요청(kt00009)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 계좌 > 계좌별주문체결현황요청(kt00009) |  |  |  |  |
| API 명 |  | 계좌별주문체결현황요청 |  |  |  |  |
| API ID |  | kt00009 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/acnt |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 계좌별 주문 체결 현황 정보를 조회합니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | ord_dt | 주문일자 | String | N | 8 | YYYYMMDD |
|  | stk_bond_tp | 주식채권구분 | String | Y | 1 | 0:전체, 1:주식, 2:채권 |
|  | mrkt_tp | 시장구분 | String | Y | 1 | 0:전체, 1:코스피, 2:코스닥, 3:OTCBB, 4:ECN |
|  | sell_tp | 매도수구분 | String | Y | 1 | 0:전체, 1:매도, 2:매수 |
|  | qry_tp | 조회구분 | String | Y | 1 | 0:전체, 1:체결 |
|  | stk_cd | 종목코드 | String | N | 12 | 전문 조회할 종목코드 |
|  | fr_ord_no | 시작주문번호 | String | N | 7 | 시작주문번호의 이전 주문은 조회 되지 않으며 약정금액에도 포함 되지 않음 |
|  | dmst_stex_tp | 국내거래소구분 | String | Y | 6 | %:(전체),KRX:한국거래소,NXT:넥스트트레이드,SOR:최선주문집행 |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | sell_grntl_engg_amt | 매도약정금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | buy_engg_amt | 매수약정금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | engg_amt | 약정금액 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | acnt_ord_cntr_prst_array | 계좌별주문체결현황배열 | LIST | N |  |  |
|  | - stk_bond_tp | 주식채권구분 | String | N | 1 |  |
|  | - ord_no | 주문번호 | String | N | 7 | 고유 주문번호 7자리 |
|  | - stk_cd | 종목번호 | String | N | 12 | 접두어 1자리 + 종목코드 6자리, 접두어(A: 주식 / J: ELW / Q: ETN) |
|  | - trde_tp | 매매구분 | String | N | 15 |  |
|  | - io_tp_nm | 주문유형구분 | String | N | 20 |  |
|  | - ord_qty | 주문수량 | String | N | 10 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 10자리 숫자 |
|  | - ord_uv | 주문단가 | String | N | 10 | 단위: 원, 좌측 0-padding 처리된 부호 포함 10자리 숫자 |
|  | - cnfm_qty | 확인수량 | String | N | 10 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 10자리 숫자 |
|  | - rsrv_oppo | 예약/반대 | String | N | 4 |  |
|  | - cntr_no | 체결번호 | String | N | 7 | 체결번호 7자리 |
|  | - acpt_tp | 접수구분 | String | N | 8 |  |
|  | - orig_ord_no | 원주문번호 | String | N | 7 | 원 주문이 없는 경우 '0000000'으로 출력 |
|  | - stk_nm | 종목명 | String | N | 20 |  |
|  | - setl_tp | 결제구분 | String | N | 8 |  |
|  | - crd_deal_tp | 신용거래구분 | String | N | 20 |  |
|  | - cntr_qty | 체결수량 | String | N | 10 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 10자리 숫자 |
|  | - cntr_uv | 체결단가 | String | N | 10 | 단위: 원, 좌측 0-padding 처리된 부호 포함 10자리 숫자 |
|  | - comm_ord_tp | 통신구분 | String | N | 8 |  |
|  | - mdfy_cncl_tp | 정정/취소구분 | String | N | 12 |  |
|  | - cntr_tm | 체결시간 | String | N | 8 | HH:mm:ss |
|  | - dmst_stex_tp | 국내거래소구분 | String | N | 6 |  |
|  | - cond_uv | 스톱가 | String | N | 10 | 단위: 원, 좌측 0-padding 처리된 부호 포함 10자리 숫자 |
| Request Example |  |  |  |  |  |  |
| {<br>    "ord_dt": "",<br>    "stk_bond_tp": "0",<br>    "mrkt_tp": "0",<br>    "sell_tp": "0",<br>    "qry_tp": "0",<br>    "stk_cd": "",<br>    "fr_ord_no": "",<br>    "dmst_stex_tp": "KRX"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "sell_grntl_engg_amt": "000000000000",<br>    "buy_engg_amt": "000000004900",<br>    "engg_amt": "000000004900",<br>    "acnt_ord_cntr_prst_array": [<br>        {<br>            "stk_bond_tp": "1",<br>            "ord_no": "0000050",<br>            "stk_cd": "A069500",<br>            "trde_tp": "시장가",<br>            "io_tp_nm": "현금매수",<br>            "ord_qty": "0000000001",<br>            "ord_uv": "0000000000",<br>            "cnfm_qty": "0000000000",<br>            "rsrv_oppo": "",<br>            "cntr_no": "0000001",<br>            "acpt_tp": "접수",<br>            "orig_ord_no": "0000000",<br>            "stk_nm": "KODEX 200",<br>            "setl_tp": "삼일결제",<br>            "crd_deal_tp": "보통매매",<br>            "cntr_qty": "0000000001",<br>            "cntr_uv": "0000004900",<br>            "comm_ord_tp": "영웅문4",<br>            "mdfy_cncl_tp": "",<br>            "cntr_tm": "13:07:47",<br>            "dmst_stex_tp": "KRX",<br>            "cond_uv": "0000000000"<br>        }<br>    ],<br>    "return_code": 0,<br>    "return_msg": "조회가 완료되었습니다"<br>} |  |  |  |  |  |  |

## kt00018 - 계좌평가잔고내역요청(kt00018)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 계좌 > 계좌평가잔고내역요청(kt00018) |  |  |  |  |
| API 명 |  | 계좌평가잔고내역요청 |  |  |  |  |
| API ID |  | kt00018 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/acnt |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 계좌 평가 잔고 내역 정보를 조회합니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | qry_tp | 조회구분 | String | Y | 1 | 1:합산, 2:개별 |
|  | dmst_stex_tp | 국내거래소구분 | String | Y | 6 | KRX:한국거래소,NXT:넥스트트레이드 |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | tot_pur_amt | 총매입금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | tot_evlt_amt | 총평가금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | tot_evlt_pl | 총평가손익금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | tot_prft_rt | 총수익률(%) | String | N | 12 | 단위: %, 소수점 둘째 자리까지 포맷된 백분율 |
|  | prsm_dpst_aset_amt | 추정예탁자산 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | tot_loan_amt | 총대출금 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | tot_crd_loan_amt | 총융자금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | tot_crd_ls_amt | 총대주금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | acnt_evlt_remn_indv_tot | 계좌평가잔고개별합산 | LIST | N |  |  |
|  | - stk_cd | 종목번호 | String | N | 12 | 접두어 1자리 + 종목코드 6자리, 접두어(A: 주식 / J: ELW / Q: ETN) |
|  | - stk_nm | 종목명 | String | N | 40 |  |
|  | - evltv_prft | 평가손익 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - prft_rt | 수익률(%) | String | N | 12 | 단위: %, 소수점 둘째 자리까지 포맷된 백분율 |
|  | - pur_pric | 매입가 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - pred_close_pric | 전일종가 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - rmnd_qty | 보유수량 | String | N | 15 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - trde_able_qty | 매매가능수량 | String | N | 15 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - cur_prc | 현재가 | String | N | 12 | 단위: 원, 좌측 0-padding 처리된 부호 포함 12자리 숫자 |
|  | - pred_buyq | 전일매수수량 | String | N | 15 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - pred_sellq | 전일매도수량 | String | N | 15 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - tdy_buyq | 금일매수수량 | String | N | 15 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - tdy_sellq | 금일매도수량 | String | N | 15 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - pur_amt | 매입금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - pur_cmsn | 매입수수료 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - evlt_amt | 평가금액 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - sell_cmsn | 평가수수료 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - tax | 세금 | String | N | 15 | 단위: 원, 좌측 0-padding 처리된 부호 포함 15자리 숫자 |
|  | - sum_cmsn | 수수료합 | String | N | 15 | 매입수수료 + 평가수수료 |
|  | - poss_rt | 보유비중(%) | String | N | 12 | 단위: %, 소수점 둘째 자리까지 포맷된 백분율 |
|  | - crd_tp | 신용구분 | String | N | 2 |  |
|  | - crd_tp_nm | 신용구분명 | String | N | 4 |  |
|  | - crd_loan_dt | 대출일 | String | N | 8 | YYYYMMDD |
| Request Example |  |  |  |  |  |  |
| {<br>    "qry_tp": "1",<br>    "dmst_stex_tp": "KRX"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "tot_pur_amt": "000000017598258",<br>    "tot_evlt_amt": "000000025789890",<br>    "tot_evlt_pl": "000000008138825",<br>    "tot_prft_rt": "46.25",<br>    "prsm_dpst_aset_amt": "000001012632507",<br>    "tot_loan_amt": "000000000000000",<br>    "tot_crd_loan_amt": "000000000000000",<br>    "tot_crd_ls_amt": "000000000000000",<br>    "acnt_evlt_remn_indv_tot": [<br>        {<br>            "stk_cd": "A005930",<br>            "stk_nm": "삼성전자",<br>            "evltv_prft": "-00000000196888",<br>            "prft_rt": "-52.71",<br>            "pur_pric": "000000000124500",<br>            "pred_close_pric": "000000045400",<br>            "rmnd_qty": "000000000000003",<br>            "trde_able_qty": "000000000000003",<br>            "cur_prc": "000000059000",<br>            "pred_buyq": "000000000000000",<br>            "pred_sellq": "000000000000000",<br>            "tdy_buyq": "000000000000000",<br>            "tdy_sellq": "000000000000000",<br>            "pur_amt": "000000000373500",<br>            "pur_cmsn": "000000000000050",<br>            "evlt_amt": "000000000177000",<br>            "sell_cmsn": "000000000000020",<br>            "tax": "000000000000318",<br>            "sum_cmsn": "000000000000070",<br>            "poss_rt": "2.12",<br>            "crd_tp": "00",<br>            "crd_tp_nm": "",<br>            "crd_loan_dt": ""<br>        },<br>        {<br>            "stk_cd": "A005930",<br>            "stk_nm": "삼성전자",<br>            "evltv_prft": "-00000000995004",<br>            "prft_rt": "-59.46",<br>            "pur_pric": "000000000209178",<br>            "pred_close_pric": "000000097600",<br>            "rmnd_qty": "000000000000008",<br>            "trde_able_qty": "000000000000008",<br>            "cur_prc": "000000085000",<br>            "pred_buyq": "000000000000000",<br>            "pred_sellq": "000000000000000",<br>            "tdy_buyq": "000000000000000",<br>            "tdy_sellq": "000000000000000",<br>            "pur_amt": "000000001673430",<br>            "pur_cmsn": "000000000000250",<br>            "evlt_amt": "000000000680000",<br>            "sell_cmsn": "000000000000100",<br>            "tax": "000000000001224",<br>            "sum_cmsn": "000000000000350",<br>            "poss_rt": "9.51",<br>            "crd_tp": "00",<br>            "crd_tp_nm": "",<br>            "crd_loan_dt": ""<br>        }<br>    ],<br>    "return_code": 0,<br>    "return_msg": "조회가 완료되었습니다"<br>} |  |  |  |  |  |  |

## ka10075 - 미체결요청(ka10075)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 계좌 > 미체결요청(ka10075) |  |  |  |  |
| API 명 |  | 미체결요청 |  |  |  |  |
| API ID |  | ka10075 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/acnt |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 미체결 내역을 조회합니다.<br>신용주문의 경우 io_tp_nm(주문구분) 속성에서 확인 가능합니다. <br>ex) io_tp_nm (주문구분) : +매수신용 |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | all_stk_tp | 전체종목구분 | String | Y | 1 | 0:전체, 1:종목 |
|  | trde_tp | 매매구분 | String | Y | 1 | 0:전체, 1:매도, 2:매수 |
|  | stk_cd | 종목코드 | String | N | 6 | 종목코드 6자리 |
|  | stex_tp | 거래소구분 | String | Y | 1 | 0 : 통합, 1 : KRX, 2 : NXT |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | oso | 미체결 | LIST | N |  |  |
|  | - acnt_no | 계좌번호 | String | N | 20 | 고유 계좌번호 10자리 숫자 |
|  | - ord_no | 주문번호 | String | N | 20 | 고유 주문번호 7자리 숫자 |
|  | - mang_empno | 관리사번 | String | N | 20 |  |
|  | - stk_cd | 종목코드 | String | N | 20 |  |
|  | - tsk_tp | 업무구분 | String | N | 20 |  |
|  | - ord_stt | 주문상태 | String | N | 20 |  |
|  | - stk_nm | 종목명 | String | N | 40 |  |
|  | - ord_qty | 주문수량 | String | N | 20 | 단위: 1주 |
|  | - ord_pric | 주문가격 | String | N | 20 | 단위: 원 |
|  | - oso_qty | 미체결수량 | String | N | 20 | 단위: 1주 |
|  | - cntr_tot_amt | 체결누계금액 | String | N | 20 | 단위: 원 |
|  | - orig_ord_no | 원주문번호 | String | N | 20 | 원 주문이 없는 경우 '0000000'으로 출력 |
|  | - io_tp_nm | 주문구분 | String | N | 20 |  |
|  | - trde_tp | 매매구분 | String | N | 20 |  |
|  | - tm | 시간 | String | N | 20 | 주문 시간, HHmmss |
|  | - cntr_no | 체결번호 | String | N | 20 |  |
|  | - cntr_pric | 체결가 | String | N | 20 | 단위: 원 |
|  | - cntr_qty | 체결량 | String | N | 20 | 단위: 1주 |
|  | - cur_prc | 현재가 | String | N | 20 |  |
|  | - sel_bid | 매도호가 | String | N | 20 | 단위: 원, 현재 첫번째 매도호가 |
|  | - buy_bid | 매수호가 | String | N | 20 | 단위: 원, 현재 첫번째 매수호가 |
|  | - unit_cntr_pric | 단위체결가 | String | N | 20 | 단위: 원 |
|  | - unit_cntr_qty | 단위체결량 | String | N | 20 | 단위: 1주 |
|  | - tdy_trde_cmsn | 당일매매수수료 | String | N | 20 | 단위: 원 |
|  | - tdy_trde_tax | 당일매매세금 | String | N | 20 | 단위: 원 |
|  | - ind_invsr | 개인투자자 | String | N | 20 |  |
|  | - stex_tp | 거래소구분 | String | N | 20 | 0 : 통합, 1 : KRX, 2 : NXT |
|  | - stex_tp_txt | 거래소구분텍스트 | String | N | 20 | 통합,KRX,NXT |
|  | - sor_yn | SOR 여부값 | String | N | 20 | Y,N |
|  | - stop_pric | 스톱가 | String | N | 20 | 스톱지정가주문 스톱가 |
| Request Example |  |  |  |  |  |  |
| {<br>    "all_stk_tp": "1",<br>    "trde_tp": "0",<br>    "stk_cd": "005930",<br>    "stex_tp": "0"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "oso": [<br>        {<br>            "acnt_no": "1234567890",<br>            "ord_no": "0000069",<br>            "mang_empno": "",<br>            "stk_cd": "005930",<br>            "tsk_tp": "",<br>            "ord_stt": "접수",<br>            "stk_nm": "삼성전자",<br>            "ord_qty": "1",<br>            "ord_pric": "0",<br>            "oso_qty": "1",<br>            "cntr_tot_amt": "0",<br>            "orig_ord_no": "0000000",<br>            "io_tp_nm": "+매수",<br>            "trde_tp": "시장가",<br>            "tm": "154113",<br>            "cntr_no": "",<br>            "cntr_pric": "0",<br>            "cntr_qty": "0",<br>            "cur_prc": "+74100",<br>            "sel_bid": "0",<br>            "buy_bid": "+74100",<br>            "unit_cntr_pric": "",<br>            "unit_cntr_qty": "",<br>            "tdy_trde_cmsn": "0",<br>            "tdy_trde_tax": "0",<br>            "ind_invsr": "",<br>            "stex_tp": "1",<br>            "stex_tp_txt": "KRX",<br>            "sor_yn": "N"<br>        }<br>    ],<br>    "return_code": 0,<br>    "return_msg": " 조회가 완료되었습니다."<br>} |  |  |  |  |  |  |

## ka10076 - 체결요청(ka10076)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 계좌 > 체결요청(ka10076) |  |  |  |  |
| API 명 |  | 체결요청 |  |  |  |  |
| API ID |  | ka10076 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/acnt |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 체결 내역을 조회합니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | stk_cd | 종목코드 | String | N | 6 | 종목코드 6자리 |
|  | qry_tp | 조회구분 | String | Y | 1 | 0:전체, 1:종목 |
|  | sell_tp | 매도수구분 | String | Y | 1 | 0:전체, 1:매도, 2:매수 |
|  | ord_no | 주문번호 | String | N | 10 | 검색 기준 값으로 입력한 주문번호 보다 과거에 체결된 내역이 조회됩니다. |
|  | stex_tp | 거래소구분 | String | Y | 1 | 0 : 통합, 1 : KRX, 2 : NXT |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | cntr | 체결 | LIST | N |  |  |
|  | - ord_no | 주문번호 | String | N | 20 | 주문번호 7자리 |
|  | - stk_nm | 종목명 | String | N | 40 |  |
|  | - io_tp_nm | 주문구분 | String | N | 20 |  |
|  | - ord_pric | 주문가격 | String | N | 20 | 단위: 원 |
|  | - ord_qty | 주문수량 | String | N | 20 | 단위: 1주 |
|  | - cntr_pric | 체결가 | String | N | 20 | 단위: 원 |
|  | - cntr_qty | 체결량 | String | N | 20 | 단위: 1주 |
|  | - oso_qty | 미체결수량 | String | N | 20 | 단위: 1주 |
|  | - tdy_trde_cmsn | 당일매매수수료 | String | N | 20 | 단위: 원 |
|  | - tdy_trde_tax | 당일매매세금 | String | N | 20 | 단위: 원 |
|  | - ord_stt | 주문상태 | String | N | 20 |  |
|  | - trde_tp | 매매구분 | String | N | 20 |  |
|  | - orig_ord_no | 원주문번호 | String | N | 20 | 원 주문이 없는 경우 '0000000'으로 출력 |
|  | - ord_tm | 주문시간 | String | N | 20 | HHmmss |
|  | - stk_cd | 종목코드 | String | N | 20 | 종목코드 6자리 |
|  | - stex_tp | 거래소구분 | String | N | 20 | 0 : 통합, 1 : KRX, 2 : NXT |
|  | - stex_tp_txt | 거래소구분텍스트 | String | N | 20 | 통합,KRX,NXT |
|  | - sor_yn | SOR 여부값 | String | N | 20 | Y,N |
|  | - stop_pric | 스톱가 | String | N | 20 | 스톱지정가주문 스톱가 |
| Request Example |  |  |  |  |  |  |
| {<br>    "stk_cd": "005930",<br>    "qry_tp": "1",<br>    "sell_tp": "0",<br>    "ord_no": "",<br>    "stex_tp": "0"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "cntr": [<br>        {<br>            "ord_no": "0000037",<br>            "stk_nm": "삼성전자",<br>            "io_tp_nm": "-매도",<br>            "ord_pric": "158200",<br>            "ord_qty": "1",<br>            "cntr_pric": "158200",<br>            "cntr_qty": "1",<br>            "oso_qty": "0",<br>            "tdy_trde_cmsn": "310",<br>            "tdy_trde_tax": "284",<br>            "ord_stt": "체결",<br>            "trde_tp": "보통",<br>            "orig_ord_no": "0000000",<br>            "ord_tm": "153815",<br>            "stk_cd": "005930",<br>            "stex_tp": "0",<br>            "stex_tp_txt": "SOR",<br>            "sor_yn": "Y"<br>        },<br>        {<br>            "ord_no": "0000036",<br>            "stk_nm": "삼성전자",<br>            "io_tp_nm": "-매도",<br>            "ord_pric": "158200",<br>            "ord_qty": "1",<br>            "cntr_pric": "158200",<br>            "cntr_qty": "1",<br>            "oso_qty": "0",<br>            "tdy_trde_cmsn": "310",<br>            "tdy_trde_tax": "284",<br>            "ord_stt": "체결",<br>            "trde_tp": "보통",<br>            "orig_ord_no": "0000000",<br>            "ord_tm": "153806",<br>            "stk_cd": "005930",<br>            "stex_tp": "0",<br>            "stex_tp_txt": "SOR",<br>            "sor_yn": "Y"<br>        }<br>    ],<br>    "return_code": 0,<br>    "return_msg": " 조회가 완료되었습니다."<br>} |  |  |  |  |  |  |

## ka10100 - 종목정보 조회(ka10100)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 종목정보 > 종목정보 조회(ka10100) |  |  |  |  |
| API 명 |  | 종목정보 조회 |  |  |  |  |
| API ID |  | ka10100 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | https://api.kiwoom.com |  |  |  |  |
| 모의투자 도메인 |  | https://mockapi.kiwoom.com(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/stkinfo |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 종목 코드로 종목 정보를 조회합니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | stk_cd | 종목코드 | String | Y | 6 | 종목코드 6자리 |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | code | 종목코드 | String | N |  | 단축코드 |
|  | name | 종목명 | String | N | 40 |  |
|  | listCount | 상장주식수 | String | N | 16 | 단위: 1주, 좌측 0-padding 처리된 부호 포함 16자리 숫자 |
|  | auditInfo | 감리구분 | String | N |  |  |
|  | regDay | 상장일 | String | N | 8 | YYYYMMDD |
|  | lastPrice | 전일종가 | String | N | 8 | 단위: 원, 좌측 0-padding 처리된 부호 포함 8자리 숫자 |
|  | state | 종목상태 | String | N |  |  |
|  | marketCode | 시장구분코드 | String | N |  |  |
|  | marketName | 시장명 | String | N |  |  |
|  | upName | 업종명 | String | N |  |  |
|  | upSizeName | 회사크기분류 | String | N |  |  |
|  | companyClassName | 회사분류 | String | N |  | 코스닥만 존재함 |
|  | orderWarning | 투자유의종목여부 | String | N |  | 0: 해당없음, 2: 정리매매, 3: 단기과열, 4: 투자위험, 5: 투자경과, 1: ETF투자주의요망(ETF인 경우만 전달 |
|  | nxtEnable | NXT가능여부 | String | N |  | Y: 가능 |
| Request Example |  |  |  |  |  |  |
| {<br>    "stk_cd": "005930"<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| {<br>    "code": "005930",<br>    "name": "삼성전자",<br>    "listCount": "0000000026034239",<br>    "auditInfo": "정상",<br>    "regDay": "20090803",<br>    "lastPrice": "00136000",<br>    "state": "증거금20%\|담보대출\|신용가능",<br>    "marketCode": "0",<br>    "marketName": "거래소",<br>    "upName": "금융업",<br>    "upSizeName": "대형주",<br>    "companyClassName": "",<br>    "orderWarning": "0",<br>    "nxtEnable": "Y",<br>    "return_code": 0,<br>    "return_msg": "정상적으로 처리되었습니다"<br>} |  |  |  |  |  |  |

## 00 - 주문체결(00)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 실시간시세 > 주문체결(00) |  |  |  |  |
| API 명 |  | 주문체결 |  |  |  |  |
| API ID |  | 00 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | wss://api.kiwoom.com:10000 |  |  |  |  |
| 모의투자 도메인 |  | wss://mockapi.kiwoom.com:10000(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/websocket |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 실시간 항목 00(주문체결)은 종목코드(item) 등록과 상관 없이 ACCESS TOKEN을 발급한 계좌에 주문 접수, 체결, 정정, 취소 등 매매가 발생할 경우 데이터가 수신됩니다. <br><br>ACCESS TOKEN 발급과 상관없이 해당 종목의 체결내역을 보고싶으신 경우에 0B를 이용 부탁드립니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | trnm | 서비스명 | String | Y | 10 | REG : 등록 , REMOVE : 해지 |
|  | grp_no | 그룹번호 | String | Y | 4 |  |
|  | refresh | 기존등록유지여부 | String | Y | 1 | 등록(REG)시<br><br>0:기존유지안함 1:기존유지(Default)<br><br>0일경우 기존등록한 item/type은 해지, 1일경우 기존등록한 item/type 유지<br><br>해지(REMOVE)시 값 불필요 |
|  | data | 실시간 등록 리스트 | LIST |  |  |  |
|  | - item | 실시간 등록 요소 | String | N | 100 |  |
|  | - type | 실시간 항목 | String | Y | 2 | TR 명(0A,0B....) |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | return_code | 결과코드 | String | N |  | 통신결과에대한 코드<br>(등록,해지요청시에만 값 전송 0:정상,1:오류 , 데이터 실시간 수신시 미전송) |
|  | return_msg | 결과메시지 | String | N |  | 통신결과에대한메시지 |
|  | trnm | 서비스명 | String | N |  | 등록,해지요청시 요청값 반환 , 실시간수신시 REAL 반환 |
|  | data | 실시간 등록리스트 | LIST | N |  |  |
|  | - type | 실시간항목 | String | N |  | TR 명(0A,0B....) |
|  | - name | 실시간 항목명 | String | N |  |  |
|  | - item | 실시간 등록 요소 | String | N |  | 종목코드 |
|  | - values | 실시간 값 리스트 | LIST | N |  |  |
|  | - - 9201 | 계좌번호 | String | N |  |  |
|  | - - 9203 | 주문번호 | String | N |  |  |
|  | - - 9205 | 관리자사번 | String | N |  |  |
|  | - - 9001 | 종목코드,업종코드 | String | N |  |  |
|  | - - 912 | 주문업무분류 | String | N |  |  |
|  | - - 913 | 주문상태 | String | N |  | 접수, 체결, 확인, 취소, 거부 |
|  | - - 302 | 종목명 | String | N |  |  |
|  | - - 900 | 주문수량 | String | N |  |  |
|  | - - 901 | 주문가격 | String | N |  |  |
|  | - - 902 | 미체결수량 | String | N |  |  |
|  | - - 903 | 체결누계금액 | String | N |  |  |
|  | - - 904 | 원주문번호 | String | N |  |  |
|  | - - 905 | 주문구분 | String | N |  | "+/-", 매도, 매수, 매도정정, 매수정정, 매수취소, 매도취소<br><br><br>※ 영웅문4에서 적색으로 표기되어있으면 +가, 청색으로 표기되어있으면 -가 앞에 기재됩니다 |
|  | - - 906 | 매매구분 | String | N |  | 보통, 시장가, 조건부지정가, 최유리지정가, 최우선지정가, 보통(IOC), 시장가(IOC), 최유리(IOC), 보통(FOK), 시장가(FOK), 최유리(FOK), 스톰지정가, 중간가, 중간가(IOC), 중간가(FOK), 장전시간외, 장후시간외, 시간외대량, 시간외바스켓, 시간외자사주, 시간외단일가 |
|  | - - 907 | 매도수구분 | String | N |  | 1:매도, 2:매수 |
|  | - - 908 | 주문/체결시간 | String | N |  |  |
|  | - - 909 | 체결번호 | String | N |  |  |
|  | - - 910 | 체결가 | String | N |  |  |
|  | - - 911 | 체결량 | String | N |  |  |
|  | - - 10 | 현재가 | String | N |  |  |
|  | - - 27 | (최우선)매도호가 | String | N |  |  |
|  | - - 28 | (최우선)매수호가 | String | N |  |  |
|  | - - 914 | 단위체결가 | String | N |  |  |
|  | - - 915 | 단위체결량 | String | N |  |  |
|  | - - 938 | 당일매매수수료 | String | N |  |  |
|  | - - 939 | 당일매매세금 | String | N |  |  |
|  | - - 919 | 거부사유 | String | N |  |  |
|  | - - 920 | 화면번호 | String | N |  |  |
|  | - - 921 | 터미널번호 | String | N |  |  |
|  | - - 922 | 신용구분 | String | N |  | 실시간 체결용 |
|  | - - 923 | 대출일 | String | N |  | 실시간 체결용 |
|  | - - 10010 | 시간외단일가_현재가 | String | N |  |  |
|  | - - 2134 | 거래소구분 | String | N |  | 0:통합,1:KRX,2:NXT |
|  | - - 2135 | 거래소구분명 | String | N |  | 통합,KRX,NXT |
|  | - - 2136 | SOR여부 | String | N |  | Y,N |
| Request Example |  |  |  |  |  |  |
| {<br>    "trnm": "REG",<br>    "grp_no": "1",<br>    "refresh": "1",<br>    "data": [<br>        {<br>            "item": [<br>                ""<br>            ],<br>            "type": [<br>                "00"<br>            ]<br>        }<br>    ]<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| #요청<br>{<br>    'trnm': 'REG',<br>    'return_code': 0,<br>    'return_msg': ''<br>}<br><br>#실시간 수신<br>{<br>    'data':[<br>        {<br>            'values': {<br>                '9201':'1111111111',<br>                '9203':'0000018',<br>                '9205':'',<br>                '9001':'005930',<br>                '912':'JJ',<br>                '913':'접수',<br>                '302':'삼성전자',<br>                '900':'1',<br>                '901':'0',<br>                '902':'1',<br>                '903':'0',<br>                '904':'0000000',<br>                '905':'+매수',<br>                '906':'시장가',<br>                '907':'2',<br>                '908':'094022',<br>                '909':'',<br>                '910':'',<br>                '911':'',<br>                '10':'+60700',<br>                '27':'+60700',<br>                '28':'-60000',<br>                '914':'',<br>                '915':'',<br>                '938':'0',<br>                '939':'0',<br>                '919':'0',<br>                '920':'',<br>                '921':'0701002',<br>                '922':'00',<br>                '923':'00000000',<br>                '10010':'',<br>                '2134':'1',<br>                '2135':'KRX',<br>                '2136':'Y'<br>            },<br>            'type':'00',<br>            'name':'주문체결',<br>            'item':'005930'<br>        }<br>    ],<br>    'trnm': 'REAL'<br>} |  |  |  |  |  |  |

## 04 - 잔고(04)

| C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | --- | --- | --- | --- | --- | --- |
| ◀ API 리스트 이동 |  |  |  |  |  |  |
| 키움 REST API |  |  |  |  |  |  |
| API 정보 |  |  |  |  |  |  |
| 메뉴 위치 |  | 국내주식 > 실시간시세 > 잔고(04) |  |  |  |  |
| API 명 |  | 잔고 |  |  |  |  |
| API ID |  | 04 |  |  |  |  |
| 기본정보 |  |  |  |  |  |  |
| Method |  | POST |  |  |  |  |
| 운영 도메인 |  | wss://api.kiwoom.com:10000 |  |  |  |  |
| 모의투자 도메인 |  | wss://mockapi.kiwoom.com:10000(KRX만 지원가능) |  |  |  |  |
| URL |  | /api/dostk/websocket |  |  |  |  |
| Format |  | JSON |  |  |  |  |
| Content-Type |  | application/json;charset=UTF-8 |  |  |  |  |
| 개요 |  |  |  |  |  |  |
| 실시간 항목 04(잔고)는 종목코드(item) 등록과 상관 없이 ACCESS TOKEN을 발급한 계좌에 주문 체결이 발생할 경우 데이터가 수신됩니다. |  |  |  |  |  |  |
| Request |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | authorization | 접근토큰 | String | Y | 1000 | 토큰 지정시 토큰타입("Bearer") 붙혀서 호출 <br> 예) Bearer Egicyx... |
|  | cont-yn | 연속조회여부 | String | N | 1 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 cont-yn값 세팅 |
|  | next-key | 연속조회키 | String | N | 50 | 응답 Header의 연속조회여부값이 Y일 경우 다음데이터 요청시 응답 Header의 next-key값 세팅 |
| Body | trnm | 서비스명 | String | Y | 10 | REG : 등록 , REMOVE : 해지 |
|  | grp_no | 그룹번호 | String | Y | 4 |  |
|  | refresh | 기존등록유지여부 | String | Y | 1 | 등록(REG)시<br> <br>0:기존유지안함 1:기존유지(Default)<br><br>0일경우 기존등록한 item/type은 해지, 1일경우 기존등록한 item/type 유지<br><br>해지(REMOVE)시 값 불필요 |
|  | data | 실시간 등록 리스트 | LIST |  |  |  |
|  | - item | 실시간 등록 요소 | String | N | 104 |  |
|  | - type | 실시간 항목 | String | Y | 2 | TR 명(0A,0B....) |
| Response |  |  |  |  |  |  |
| 구분 | Element | 한글명 | Type | Required | Length | Description |
| Header | api-id | TR명 | String | Y | 10 | 7자리 TR코드, ex) ka00001 |
|  | cont-yn | 연속조회여부 | String | N | 1 | 다음 데이터가 있을시 Y값 전달 |
|  | next-key | 연속조회키 | String | N | 50 | 다음 데이터가 있을시 다음 키값 전달 |
| Body | return_code | 결과코드 | String | N |  | 통신결과에대한 코드<br>(등록,해지요청시에만 값 전송 0:정상,1:오류 , 데이터 실시간 수신시 미전송) |
|  | return_msg | 결과메시지 | String | N |  | 통신결과에대한메시지 |
|  | trnm | 서비스명 | String | N |  | 등록,해지요청시 요청값 반환 , 실시간수신시 REAL 반환 |
|  | data | 실시간 등록리스트 | LIST | N |  |  |
|  | - type | 실시간항목 | String | N |  | TR 명(0A,0B....) |
|  | - name | 실시간 항목명 | String | N |  |  |
|  | - item | 실시간 등록 요소 | String | N |  | 종목코드 |
|  | - values | 실시간 값 리스트 | LIST | N |  |  |
|  | - - 9201 | 계좌번호 | String | N |  |  |
|  | - - 9001 | 종목코드,업종코드 | String | N |  |  |
|  | - - 917 | 신용구분 | String | N |  |  |
|  | - - 916 | 대출일 | String | N |  |  |
|  | - - 302 | 종목명 | String | N |  |  |
|  | - - 10 | 현재가 | String | N |  |  |
|  | - - 930 | 보유수량 | String | N |  |  |
|  | - - 931 | 매입단가 | String | N |  |  |
|  | - - 932 | 총매입가(당일누적) | String | N |  |  |
|  | - - 933 | 주문가능수량 | String | N |  |  |
|  | - - 945 | 당일순매수량 | String | N |  |  |
|  | - - 946 | 매도/매수구분 | String | N |  | 계약,주 |
|  | - - 950 | 당일총매도손익 | String | N |  |  |
|  | - - 951 | Extra Item | String | N |  |  |
|  | - - 27 | (최우선)매도호가 | String | N |  |  |
|  | - - 28 | (최우선)매수호가 | String | N |  |  |
|  | - - 307 | 기준가 | String | N |  |  |
|  | - - 8019 | 손익률(실현손익) | String | N |  |  |
|  | - - 957 | 신용금액 | String | N |  |  |
|  | - - 958 | 신용이자 | String | N |  |  |
|  | - - 918 | 만기일 | String | N |  |  |
|  | - - 990 | 당일실현손익(유가) | String | N |  |  |
|  | - - 991 | 당일실현손익율(유가) | String | N |  |  |
|  | - - 992 | 당일실현손익(신용) | String | N |  |  |
|  | - - 993 | 당일실현손익율(신용) | String | N |  |  |
|  | - - 959 | 담보대출수량 | String | N |  |  |
|  | - - 924 | Extra Item | String | N |  |  |
| Request Example |  |  |  |  |  |  |
| {<br>    "trnm": "REG",<br>    "grp_no": "1",<br>    "refresh": "1",<br>    "data": [<br>        {<br>            "item": [<br>                ""<br>            ],<br>            "type": [<br>                "04"<br>            ]<br>        }<br>    ]<br>} |  |  |  |  |  |  |
| Response Example |  |  |  |  |  |  |
| #요청<br>{<br>    'trnm': 'REG',<br>    'return_code': 0,<br>    'return_msg': ''<br>}<br><br>#실시간 수신<br>{<br>    'data': [<br>        {<br>            'values': {<br>                '9201': '1111111111',<br>                '9001': '005930',<br>                '917': '00',<br>                '916': '00000000',<br>                '302': '삼성전자',<br>                '10': '-60150',<br>                '930': '102',<br>                '931': '154116',<br>                '932': '15719834',<br>                '933': '102',<br>                '945': '4',<br>                '946': '2',<br>                '950': '0',<br>                '951': '0',<br>                '27': '-60200',<br>                '28': '-60100',<br>                '307': '60300',<br>                '8019': '0.00',<br>                '957': '0',<br>                '958': '0',<br>                '918': '00000000',<br>                '990': '0',<br>                '991': '0.00',<br>                '992': '0',<br>                '993': '0.00',<br>                '959': '0',<br>                '924': '0'<br>            },<br>            'type': '04',<br>            'name': '현물잔고',<br>            'item': '005930'<br>        }<br>    ],<br>    'trnm': 'REAL'<br>} |  |  |  |  |  |  |
