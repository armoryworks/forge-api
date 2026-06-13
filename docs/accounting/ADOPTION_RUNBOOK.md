# Forge Accounting — Adoption Runbook

> **Audience:** the person leading a client install's adoption of the built-in Forge GL
> (owner/implementer + the client's controller/CPA). **Status:** owner-ratified direction
> (§10 resolution log, 2026-06-12): the cutover strategy is an **adoption-time client
> decision** — this runbook documents both paths; nothing in code forces either.

---

## 0. The decision the client makes

| | **Parallel run** (recommended for a first adoption) | **Hard cut** |
|---|---|---|
| What it is | Run Forge GL alongside the incumbent (e.g. QuickBooks) for **one month**, reconcile both at month-end, then retire the incumbent | Pick a cutover date; the incumbent stops being written the day Forge GL goes live |
| Cost | Double entry effort for the overlap month (mitigated: Forge posts automatically from operations; the incumbent only needs its usual feed) | None |
| Risk | Low — discrepancies surface while the incumbent is still authoritative | Higher — errors found after cutover are corrected in Forge only |
| Choose when | First GL adoption, CPA wants evidence, complex opening balances | Clean books, simple chart, experienced controller |

Either way, the go-live sequence below is identical — parallel run just delays "retire the
incumbent" by one reconciled month.

---

## 1. Pre-go-live checklist (all installs)

1. **Chart of accounts review.** The seeded small-manufacturer chart + `AccountDeterminationRule`
   rows are the posting map. Walk the client's CPA through `GET /accounting/accounts` and the §7
   posting matrix; add client-specific accounts BEFORE go-live (renumbering after history exists
   is painful).
2. **Standard costs populated.** Run `POST /parts/{id}/recalculate-standard-cost` across the part
   master (the rollup needs routings/work-center rates/BOMs captured first). Degenerate standards
   (material-only, zero labor/overhead) make every job close swing to variance.
   - Open decision to ratify per install: **BOMLine vs BomRevision authority** for costing.
3. **Fiscal calendar.** Verify the seeded FiscalYear/periods match the client's year (calendar vs
   fiscal). Adjust before any posting.
4. **Capability + role prep.** Assign the `Controller` role; confirm SoD pairs exist (two people
   minimum for: journal maker/checker over threshold, bank-account change/approve, batch
   create/release).
5. **Settings.** Banking group (NACHA origination from the bank's ACH agreement; exposure limit;
   prenote policy), variance watchdog thresholds, QBO export choice (CSV always works;
   `CAP-ACCT-QBO-EXPORT` only if the CPA wants API delivery).

## 2. Opening balances (§7A conversion)

1. Pick the **conversion date** (a period boundary; month-end strongly preferred).
2. From the incumbent, export the trial balance as of that date.
3. Post the §7A conversion journal (`Source = Conversion`) — one balanced entry loading every
   account's opening balance. Control accounts need their party detail:
   - **AR:** one open item per unpaid customer invoice (re-key the open invoices in Forge so the
     sub-ledger ties to the control opening balance).
   - **AP:** one open item per unpaid vendor bill (same — enter as standalone vendor bills dated
     with their original document dates so aging is honest).
   - **Inventory:** load the valuation store per part at standard; the GL inventory control must
     equal Σ store values.
4. **Tie-out:** Forge trial balance as of conversion date == incumbent trial balance, line by
   line. AR/AP aging == control balances (`reconciliation.isReconciled == true` on both aging
   endpoints). Do not proceed until exact.

## 3. Go-live flips (in this order)

1. `CAP-ACCT-FULLGL` **on** — postings begin from operational events (invoices, bills, receipts,
   issues, payments, expenses). Everything before this stays dark by design.
2. Smoke the §7 matrix the same day: send an invoice, approve a bill, record a payment — confirm
   journals, open items, and aging move.
3. `CAP-RPT-FINANCIALS` **on** once the first day's postings look right — statements go live.
4. (When the bank agreement is in place) `CAP-BANK-NACHA` **on** — vendor bank accounts +
   prenotes first, live batches after prenote windows pass.

## 4. Parallel-run month (if chosen)

- Operations run in Forge normally; the incumbent receives whatever feed it received before.
- Weekly: compare AR aging, AP aging, cash balances between systems; chase any drift the week it
  appears (it compounds).
- Month-end: close the period in BOTH systems; reconcile the trial balances. Differences must be
  explained (usually timing or the incumbent's manual entries) or fixed.
- Sign-off: client controller + CPA sign the reconciled month → retire the incumbent (read-only).

## 5. Month-end close runbook (steady state)

1. **Cut-off:** all receipts, issues, shipments, and approvals for the month entered.
2. **Job closes:** `POST /accounting/jobs/{id}/close-production-cost` for jobs finishing in the
   period (absorbs labor/overhead/subcontract, sweeps WIP variance).
3. **Overhead pool:** record actual overhead, then `POST /accounting/overhead/close` (spending
   variance).
4. **Bank:** import the month's statements (`/accounting/bank-statements/import`), confirm
   matches, run the bank reconciliation worksheet to zero difference, finalize.
5. **Aging tie-outs:** AR + AP aging `isReconciled == true`; GRNI reconciliation variance within
   tolerance.
6. **Variance review:** `GET /accounting/variances` for the period — investigate anything the
   watchdog flagged during the month.
7. **Accruals/recurring:** post template journals; auto-reversing accruals as needed.
8. **Soft close** the period → review the P&L/BS with the variance story → **hard close**.
9. Exports for the CPA: trial-balance/gl-detail/journal-summary CSVs (or QBO push if enabled).

## 6. Year-end

- Close all 12 periods (hard), then `POST /accounting/years/{id}/close` — the retained-earnings
  roll posts automatically and the year locks. The closing entry is excluded from the closed
  year's P&L by design.

## 7. Rollback posture

- Before `CAP-ACCT-FULLGL` is flipped, there is nothing to roll back (dark).
- After go-live: corrections are **reversals, never edits** (immutability is enforced by
  interceptor + DB triggers). A failed adoption month under parallel run = keep the incumbent
  authoritative, fix root causes, re-run the month. There is no "wipe and retry" path once real
  postings exist — that is by design.
