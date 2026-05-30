# Locked Reference: Guide AI Answer for SOR Balance Sync With MTS

Locked on: 2026-05-30

Source:
- User-provided answer copied from Kiwoom Guide AI.
- Purpose: keep the external guide answer as a fixed reference for matching TradingDashboard holdings and balance display with MTS in SOR ON operation.

Lock rule:
- This document is a source/reference record.
- Do not edit this document after locking.
- If a newer official Kiwoom answer or live verification supersedes it, create a new dated reference instead.

## Goal

Build TradingDashboard balance and holdings display so that it matches MTS as closely as possible under SOR ON operation.

This reference is about holdings, account balance, evaluation amount, profit/loss, and account sync. It is not about deciding entry/exit strategy.

## Recommended TR Combination

Main balance/evaluation source:

- `kt00018` account evaluation balance detail

Reason:

- Closest to MTS account evaluation values.
- Provides account-level evaluation amount, profit/loss, profit rate, current price, holding quantity, orderable quantity, and average buy price.

Immediate realtime source:

- WebSocket realtime `04` balance event

Reason:

- Updates immediately after order/fill events.
- Suitable for temporary UI updates before REST reconciliation.

Supplementary/check source:

- `kt00005` execution balance
- `ka10075` open orders
- `ka10076` fills

Reason:

- Useful for checking fills, unfilled quantity, order status, and SOR confirmation.
- Not the main MTS-style evaluation source.

## Exchange Parameter Policy

The balance TR documents show `dmst_stex_tp` as `KRX` / `NXT`.

Guide AI recommendation:

1. First try `kt00018` with `dmst_stex_tp = "KRX"`.
2. Compare returned holdings and evaluation values with MTS.
3. If KRX-only does not match, query both `KRX` and `NXT`.
4. Merge by normalized 6-digit stock code.

Important caution:

- SOR orders are internally executed on KRX or NXT.
- Whether a single KRX balance query returns integrated SOR holdings can depend on account/environment.
- For maximum correctness, KRX+NXT merge is the safe fallback.

## Final Value Priority

For final confirmed holdings and MTS-style evaluation:

1. Use `kt00018` as the primary confirmed snapshot.
2. Use realtime `04` only as immediate/temporary reflection.
3. Use `kt00005`, `ka10075`, and `ka10076` to check fill/open-order details.
4. Reconcile periodically with `kt00018`.

## Realtime and Periodic Sync Strategy

Realtime immediate update:

- Trigger: WebSocket `04`
- Use for:
  - holding quantity
  - average buy price
  - orderable quantity
  - temporary current balance display

Periodic correction:

- Trigger:
  - regular timer
  - after fill event with short delay
  - manual refresh
  - post-close correction
- Main call:
  - `kt00018(qry_tp = "1", dmst_stex_tp = "KRX")`
- Suggested live verification:
  - If MTS mismatch appears, also call `kt00018(..., dmst_stex_tp = "NXT")` and merge.

Recommended correction intervals from the guide answer:

- Regular correction: 1 to 5 minutes, or event based.
- More conservative UI correction option: 15 minutes.
- After fill: delayed correction, for example after 35 seconds.
- Post-close: run a separate post-close correction.

## Evaluation Price Policy

Default recommendation:

- Use `kt00018` evaluation current price, evaluation amount, profit/loss, and profit rate as-is.

Reason:

- MTS uses broker-side account evaluation policy.
- Recalculating locally from NXT/SOR prices can diverge from MTS.

Exception:

- For NXT possible stocks, if MTS clearly reflects NXT/SOR prices differently and `kt00018` does not match, verify by KRX+NXT query and only then apply a controlled overlay.

TradingDashboard interpretation:

- Do not overwrite confirmed `kt00018` MTS-style account values casually.
- NXT/SOR market-data overlays may be useful for display, but they must be separated from confirmed account evaluation values.

## Time-Of-Day Notes

During KRX regular session:

- Realtime fills and realtime balance update the UI.
- Periodic `kt00018` corrects account values.

During NXT session:

- NXT fills/prices may require KRX+NXT balance validation.
- SOR fill confirmation should still use `00`, `ka10075`, and `ka10076`.

After market close until next day 07:00:

- Settlement and D+1/D+2 cash values can change.
- Account evaluation may become fixed or change after settlement processing.
- Run post-close and early-morning correction with `kt00018`.

## Recommended Flow

### 1. Initial Load

Call:

```json
{
  "api-id": "kt00018",
  "body": {
    "qry_tp": "1",
    "dmst_stex_tp": "KRX"
  }
}
```

Use this to build the initial holdings and account evaluation screen.

Optional:

- Confirm account identity with account-related API if needed.

### 2. Realtime Fill Reflection

Listen to:

- WebSocket `00`
- `ka10075`
- `ka10076`

Use for:

- order/fill status
- unfilled quantity
- SOR confirmation

SOR confirmation fields:

- `sor_yn`
- `stex_tp_txt`
- realtime `00` field `2136`

### 3. Realtime Balance Reflection

Listen to:

- WebSocket `04`

Use for immediate UI update:

- holding quantity
- average buy price
- orderable quantity
- current price

### 4. Periodic Correction

Call:

- `kt00018`

Recommended triggers:

- periodic timer
- fill event plus delay
- manual refresh

Use `kt00018` to correct:

- total evaluation amount
- total profit/loss
- total profit rate
- holding quantity
- orderable quantity
- average buy price
- per-stock profit/loss and profit rate

### 5. Post-Close Correction

Call:

- `kt00018` after close
- optionally again before next-day 07:00

Reason:

- settlement and account values can change after close.

## Core Fields

### `kt00018` input

```json
{
  "qry_tp": "1",
  "dmst_stex_tp": "KRX"
}
```

### `kt00018` important output fields

Account summary:

- `tot_pur_amt`
- `tot_evlt_amt`
- `tot_evlt_pl`
- `tot_prft_rt`

Holding list:

- `acnt_evlt_remn_indv_tot[].stk_cd`
- `acnt_evlt_remn_indv_tot[].stk_nm`
- `acnt_evlt_remn_indv_tot[].rmnd_qty`
- `acnt_evlt_remn_indv_tot[].trde_able_qty`
- `acnt_evlt_remn_indv_tot[].cur_prc`
- `acnt_evlt_remn_indv_tot[].pur_pric`
- `acnt_evlt_remn_indv_tot[].evlt_amt`
- `acnt_evlt_remn_indv_tot[].evltv_prft`
- `acnt_evlt_remn_indv_tot[].prft_rt`

### `kt00005` important output fields

Account/cash:

- `entr`
- `ord_alowa`

Holding list:

- `stk_cntr_remn[].stk_cd`
- `stk_cntr_remn[].cur_qty`
- `stk_cntr_remn[].buy_uv`
- `stk_cntr_remn[].pur_amt`
- `stk_cntr_remn[].evlt_amt`
- `stk_cntr_remn[].evltv_prft`
- `stk_cntr_remn[].pl_rt`

### WebSocket `04` important fields

- `9201`: account
- `9001`: stock code
- `10`: current price
- `930`: holding quantity
- `931`: average buy price
- `933`: orderable quantity

### SOR confirmation fields

- `ka10075[].sor_yn`
- `ka10075[].stex_tp_txt`
- `ka10076[].sor_yn`
- `ka10076[].stex_tp_txt`
- realtime `00` field `2136`

## TradingDashboard Fixed Interpretation

For MTS-style holdings and balance display:

1. `kt00018` is the primary confirmed source.
2. WebSocket `04` is immediate display/update source.
3. `kt00005` is a supplementary validation source.
4. `ka10075` and `ka10076` are order/fill validation sources.
5. Start with `kt00018(KRX)`.
6. If MTS mismatch appears, validate `kt00018(KRX) + kt00018(NXT)` merged by normalized 6-digit code.
7. Avoid local recalculation of MTS account evaluation unless live comparison proves it is needed.
8. Keep confirmed account evaluation separate from market-data overlays.

