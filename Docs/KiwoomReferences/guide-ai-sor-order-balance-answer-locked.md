# Locked Reference: Guide AI Answer for SOR Order and Balance

Locked on: 2026-05-30

Source:
- User-provided answer copied from Kiwoom Guide AI.
- Original attachment path at capture time:
  `C:\Users\beint\.codex\attachments\c0b8ebb5-cdad-4a69-929a-1e38c3836c3c\pasted-text.txt`
- Purpose: keep the external guide answer as a fixed reference before porting SOR order, sell, balance, and holding-check logic into TradingDashboard.

Lock rule:
- This document is a source/reference record.
- Do not edit the interpretation below unless a newer official Kiwoom reference or live test result explicitly supersedes it.
- If superseded, create a new dated document instead of modifying this one.

## Guide AI Answer Summary

The Guide AI answer agrees with the current TradingDashboard SOR-ON assumption:

- SOR order routing is sent through `dmst_stex_tp = "SOR"`.
- `stk_cd` for orders should be the normal 6-digit stock code.
- Do not use `_AL` or `_NX` suffixes in order request `stk_cd`.
- `_AL` and `_NX` remain market-data/query code conventions, not order code conventions.

## Buy and Sell Orders

Use:

- Buy: `kt10000`
- Sell: `kt10001`
- Endpoint: `POST /api/dostk/ordr`
- Header:
  - `authorization: Bearer [access token]`
  - `api-id: kt10000` or `kt10001`

Recommended SOR ON order body:

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

Field notes:

- `dmst_stex_tp`: use `SOR`
- `stk_cd`: 6-digit stock code
- `ord_qty`: order quantity
- `ord_uv`: order unit price; blank for market order
- `trde_tp`: trade type; `3` means market order in the provided example
- `cond_uv`: stop/condition price when needed

## Modify and Cancel

Use:

- Modify: `kt10002`
- Cancel: `kt10003`
- Endpoint: `POST /api/dostk/ordr`

Guide AI recommendation:

- If the original order was sent as SOR, modify/cancel with `dmst_stex_tp = "SOR"`.
- Manage modify/cancel by original order routing first.
- Only use a specific venue value such as `KRX` or `NXT` if a later official reference or live test proves Kiwoom requires the final execution venue for that case.

Modify example:

```json
{
  "dmst_stex_tp": "SOR",
  "orig_ord_no": "0000123",
  "stk_cd": "005930",
  "mdfy_qty": "1",
  "mdfy_uv": "61000",
  "mdfy_cond_uv": ""
}
```

Cancel example:

```json
{
  "dmst_stex_tp": "SOR",
  "orig_ord_no": "0000123",
  "stk_cd": "005930",
  "cncl_qty": "1"
}
```

## Order and Fill Confirmation

Use integrated exchange query mode for SOR tracking.

### Open Orders

- TR: `ka10075`
- Endpoint: `POST /api/dostk/acnt`
- Request `stex_tp = "0"` for integrated query

Example:

```json
{
  "all_stk_tp": "1",
  "trde_tp": "0",
  "stk_cd": "005930",
  "stex_tp": "0"
}
```

Important response fields:

- `oso[].ord_no`
- `oso[].stk_cd`
- `oso[].ord_qty`
- `oso[].oso_qty`
- `oso[].stex_tp`
- `oso[].stex_tp_txt`
- `oso[].sor_yn`

SOR confirmation rule:

- `sor_yn == "Y"` means the order is SOR-related.
- `stex_tp_txt == "SOR"` is also a SOR confirmation signal.

### Filled Orders

- TR: `ka10076`
- Endpoint: `POST /api/dostk/acnt`
- Request `stex_tp = "0"` for integrated query

Example:

```json
{
  "stk_cd": "005930",
  "qry_tp": "1",
  "sell_tp": "0",
  "ord_no": "",
  "stex_tp": "0"
}
```

Important response fields:

- `cntr[].ord_no`
- `cntr[].cntr_pric`
- `cntr[].cntr_qty`
- `cntr[].ord_stt`
- `cntr[].stex_tp`
- `cntr[].stex_tp_txt`
- `cntr[].sor_yn`

SOR confirmation rule:

- `sor_yn == "Y"` or `stex_tp_txt == "SOR"` confirms SOR order/fill handling.

## Realtime Order Events

Use realtime WebSocket item `00` for account order/fill events.

Registration pattern:

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

Important fields from `values`:

- `9203`: order number
- `913`: order status
- `908`: order/fill time
- `910`: fill price
- `911`: fill quantity
- `2134`: exchange code (`0` integrated, `1` KRX, `2` NXT)
- `2135`: exchange text
- `2136`: SOR flag (`Y` or `N`)

Realtime SOR confirmation:

- Prefer `2136 == "Y"`.
- Also accept `2135 == "SOR"` when provided.

## Balance and Holdings

Relevant TRs:

- `kt00005`: account execution/balance snapshot
- `kt00018`: account evaluation/balance detail
- realtime `04`: balance update events

Guide AI answer:

- `kt00005` / `kt00018` documents show `dmst_stex_tp` as `KRX` / `NXT`.
- In SOR ON operation, a single `KRX` balance query may return integrated holdings depending on Kiwoom/account behavior.
- Because this can differ by environment, verify with the actual account.
- If a single `KRX` query is not reliable, query `KRX` and `NXT` separately and merge safely.

Recommended handling:

1. Use realtime `04` for immediate holding changes after fills.
2. Use `kt00018` with `qry_tp = "1"` and `dmst_stex_tp = "KRX"` for periodic/account-summary sync.
3. Confirm in the live account whether `KRX` returns integrated holdings.
4. If not confirmed, use separate KRX/NXT balance queries and merge by normalized 6-digit stock code.

Realtime `04` important fields:

- `9001`: stock code
- `930`: holding quantity
- `931`: average buy price
- `933`: orderable quantity
- `10`: current price
- `8019`: profit rate

`kt00018` important fields:

- `tot_evlt_amt`
- `acnt_evlt_remn_indv_tot[].stk_cd`
- `acnt_evlt_remn_indv_tot[].rmnd_qty`
- `acnt_evlt_remn_indv_tot[].trde_able_qty`
- `acnt_evlt_remn_indv_tot[].cur_prc`
- `acnt_evlt_remn_indv_tot[].pur_pric`
- `acnt_evlt_remn_indv_tot[].prft_rt`

## Final Recommended Flow

1. Send order:
   - `kt10000` / `kt10001`
   - `dmst_stex_tp = "SOR"`
   - 6-digit `stk_cd`
2. Save returned `ord_no`.
3. Listen to realtime `00`:
   - order status
   - fill price/quantity
   - SOR flag through `2136`
4. Query if needed:
   - open orders: `ka10075`, `stex_tp = "0"`
   - fills: `ka10076`, `stex_tp = "0"`
5. Update holdings immediately from realtime `04`.
6. Periodically reconcile holdings through `kt00018` or `kt00005`.
7. Do not use market-data suffixes (`_NX`, `_AL`) in order requests.
8. Keep all numeric fields parsed as strings first, then convert safely.

## TradingDashboard Fixed Interpretation

This guide answer supports the current TradingDashboard implementation direction:

- SOR ON order market: `SOR`
- Order code format: 6-digit stock code
- Modify/cancel route: original SOR route
- SOR confirmation: `sor_yn`, `stex_tp_txt`, realtime `2136`
- Holding source: realtime `04` plus periodic `kt00018`/`kt00005`
- Balance merge behavior: must be live-verified before assuming KRX-only is always integrated

