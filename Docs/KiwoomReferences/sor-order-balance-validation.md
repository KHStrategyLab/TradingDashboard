# SOR Order and Balance Validation

Date: 2026-05-30

Purpose: verify the current TradingDashboard SOR-ON trading assumptions against the Kiwoom REST API reference and the previous KHStrategyLab records before porting any order logic.

## Source References

- Local extracted reference: `Docs/KiwoomReferences/sor-order-balance-reference.md`
- Source workbook: local Kiwoom REST API Excel workbook under `Docs`
- Official Kiwoom guide spot-check: `kt10000` stock buy order page confirms `dmst_stex_tp` accepts `KRX,NXT,SOR`.
- Previous KHStrategyLab records:
  - `C:\Users\beint\source\repos\KHStrategyLab\Docs\KHStrategyLab_Architecture_Guide.md`
  - `C:\Users\beint\source\repos\KHStrategyLab\Docs\KHStrategyLab_Strategy_Manual.md`
  - `C:\Users\beint\source\repos\KHStrategyLab\Docs\Archive\KHStrategyLab_save1_front_balance_screen_record.md`

## Confirmed: Buy and Sell Orders

SOR order routing is an order request field, not a stock-code suffix.

Use:

- Buy: `kt10000`
- Sell: `kt10001`
- Endpoint: `POST /api/dostk/ordr`
- Required request field: `dmst_stex_tp`
- Valid values: `KRX`, `NXT`, `SOR`
- Stock code: use the normal 6-digit stock code in `stk_cd`
- Do not use `_AL` as an order stock-code suffix unless a future official reference explicitly says so.

Required body fields for both buy and sell:

```json
{
  "dmst_stex_tp": "SOR",
  "stk_cd": "005930",
  "ord_qty": "1",
  "ord_uv": "",
  "trde_tp": "3",
  "cond_uv": ""
}
```

Important `trde_tp` values:

- `0`: limit
- `3`: market
- `5`: conditional limit
- `61`: pre-open after-hours
- `62`: after-hours single price
- `81`: after-close after-hours
- `10`, `13`, `16`: IOC variants
- `20`, `23`, `26`: FOK variants
- `28`: stop limit
- `29`, `30`, `31`: midpoint variants

Response fields include:

- `ord_no`
- `dmst_stex_tp`
- `return_code`
- `return_msg`

## Confirmed: Modify and Cancel

Modification and cancellation also carry exchange routing.

- Modify: `kt10002`
- Cancel: `kt10003`
- Endpoint: `POST /api/dostk/ordr`
- Required field: `dmst_stex_tp`
- Valid values: `KRX`, `NXT`, `SOR`

For SOR-originated orders, pass `dmst_stex_tp = "SOR"` when modifying or cancelling unless live verification proves Kiwoom requires the executed venue instead.

## Confirmed: Order and Fill Queries

Use integrated query mode for SOR order tracking.

### Open Orders

- TR: `ka10075`
- Endpoint: `POST /api/dostk/acnt`
- Key request field: `stex_tp`
- Values:
  - `0`: integrated
  - `1`: KRX
  - `2`: NXT

For SOR order status, query with:

```json
{
  "all_stk_tp": "1",
  "trde_tp": "0",
  "stk_cd": "005930",
  "stex_tp": "0"
}
```

Important response fields:

- `ord_no`
- `ord_stt`
- `ord_qty`
- `oso_qty`
- `cntr_pric`
- `cntr_qty`
- `stex_tp`
- `stex_tp_txt`
- `sor_yn`

### Filled Orders

- TR: `ka10076`
- Endpoint: `POST /api/dostk/acnt`
- Key request field: `stex_tp`
- Use `stex_tp = "0"` for integrated/SOR confirmation.

Important response fields:

- `ord_no`
- `ord_pric`
- `ord_qty`
- `cntr_pric`
- `cntr_qty`
- `oso_qty`
- `ord_stt`
- `ord_tm`
- `stk_cd`
- `stex_tp`
- `stex_tp_txt`
- `sor_yn`

The reference example shows a SOR fill as:

```json
{
  "stex_tp": "0",
  "stex_tp_txt": "SOR",
  "sor_yn": "Y"
}
```

## Confirmed: Realtime Order Events

Realtime item `00` is account order/fill events. It is not stock tick data.

Registration:

```json
{
  "trnm": "REG",
  "grp_no": "1",
  "refresh": "1",
  "data": [
    {
      "item": [""],
      "type": ["00"]
    }
  ]
}
```

Important fields:

- `9203`: order number
- `9001`: stock code
- `913`: order status
- `900`: order quantity
- `901`: order price
- `902`: unfilled quantity
- `904`: original order number
- `905`: order side/type text
- `906`: trade type text
- `907`: sell/buy flag (`1` sell, `2` buy)
- `908`: order/fill time
- `909`: fill number
- `910`: fill price
- `911`: fill quantity
- `938`: fee
- `939`: tax
- `2134`: exchange type (`0` integrated, `1` KRX, `2` NXT)
- `2135`: exchange text
- `2136`: SOR flag (`Y` or `N`)

## Confirmed: Realtime Balance Events

Realtime item `04` is account balance changes after order/fill events.

Registration:

```json
{
  "trnm": "REG",
  "grp_no": "1",
  "refresh": "1",
  "data": [
    {
      "item": [""],
      "type": ["04"]
    }
  ]
}
```

Important fields:

- `9001`: stock code
- `302`: stock name
- `10`: current price
- `930`: holding quantity
- `931`: average buy price
- `932`: total purchase amount
- `933`: orderable quantity
- `945`: today net sell quantity
- `946`: sell/buy flag
- `950`: today total sell proceeds
- `27`: best ask
- `28`: best bid
- `307`: base price
- `8019`: profit rate

## Balance Query Caution

Balance TRs are not documented as `SOR` balance calls in the extracted reference.

### `kt00005`

- Endpoint: `POST /api/dostk/acnt`
- Required field: `dmst_stex_tp`
- Documented values: `KRX`, `NXT`
- Main list: `stk_cntr_remn`
- Important fields: `stk_cd`, `stk_nm`, `cur_qty`, `cur_prc`, `buy_uv`, `pur_amt`, `evlt_amt`, `evltv_prft`, `pl_rt`

### `kt00018`

- Endpoint: `POST /api/dostk/acnt`
- Required field: `dmst_stex_tp`
- Documented values: `KRX`, `NXT`
- Main list: `acnt_evlt_remn_indv_tot`
- Important fields: `stk_cd`, `stk_nm`, `rmnd_qty`, `trde_able_qty`, `cur_prc`, `pur_pric`, `pred_close_pric`, `evlt_amt`, `evltv_prft`, `prft_rt`

Previous KHStrategyLab records use `kt00005` with KRX as the single account-balance baseline, then apply NXT/current evaluation overlay separately. That previous rule is consistent with the extracted balance TR limitation, but it still needs live verification after a real or test SOR fill.

## Current Decision for TradingDashboard

For SOR ON:

1. Send buy/sell orders through `kt10000` / `kt10001` with `dmst_stex_tp = "SOR"`.
2. Keep stock codes as normal 6-digit codes in order requests.
3. Track order/fill status through:
   - realtime `00`
   - `ka10075` with `stex_tp = "0"`
   - `ka10076` with `stex_tp = "0"`
4. Treat `sor_yn = "Y"` and/or `stex_tp_txt = "SOR"` as SOR confirmation.
5. Use realtime `04` for balance changes after fills.
6. Use `kt00005` or `kt00018` only as account balance snapshots, not as proof that an order was routed through SOR.
7. Do not let market-data suffixes (`_NX`, `_AL`) leak into order request `stk_cd`.
8. Keep KRX previous close as the locked screen base price. Order/fill/balance TRs must not overwrite the screen base-price owner.

## Implementation Start

Initial trading-layer classes were added under:

- `Services/Trading/KiwoomTradingConstants.cs`
- `Services/Trading/KiwoomTradingModels.cs`
- `Services/Trading/KiwoomTradingJson.cs`
- `Services/Trading/KiwoomTradingClient.cs`

Current scope:

- Order request construction for `kt10000`, `kt10001`, `kt10002`, `kt10003`
- SOR normalization: incoming `NXT` order intent is routed as `SOR`
- Order stock-code normalization: strip `A`, `_NX`, `_AL`, then require a 6-digit code
- Open-order query: `ka10075` with integrated `stex_tp = "0"`
- Fill query: `ka10076` with integrated `stex_tp = "0"`
- Balance snapshots: `kt00018` and `kt00005`
- Numeric parsing from Kiwoom string fields
- Internal REST pacing at 5 requests per second

Safety state:

- The trading client is not wired to strategy execution or UI order buttons yet.
- No live order is sent unless future code explicitly calls `BuyAsync` or `SellAsync`.
- Build verification passed with 0 warnings and 0 errors after adding the layer.

## Items Still Requiring Live Verification

- A SOR market buy returns `dmst_stex_tp = "SOR"` in `kt10000`.
- Realtime `00` emits `2136 = "Y"` for a SOR-routed order.
- `ka10075` reports SOR open orders with `sor_yn = "Y"` when queried using `stex_tp = "0"`.
- `ka10076` reports SOR fills with `stex_tp_txt = "SOR"` and `sor_yn = "Y"`.
- After a SOR fill, `kt00005` KRX snapshot and realtime `04` agree on holding quantity.
- If NXT execution price differs from KRX screen price, balance evaluation must stay in the evaluation overlay layer and must not rewrite order/fill facts.
