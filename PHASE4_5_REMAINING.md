# Accounting suite — remaining workstreams (after the Phases 3–5 / holes build)

Everything below is the **last** of the ACCOUNTING_SUITE_PLAN. As of this run, what's **done** (dark,
branch `feat/accounting-gl-phase1`, no migration applied, nothing deployed):

- **Phase 2 STAGE A–E** complete (AP, COGS, PO-receipt/GRNI, 3-way-match/PPV, GRNI recon, valuation store).
- **Hardening H1–H3** (dup-vendor-invoice, AP void/reversal, BilledQuantity FOR UPDATE lock).
- **Phase 3 complete incl. all §12 holes** — period close, year-end RE roll (+ statement interplay), indirect
  cash flow, bank reconciliation, fiscal-calendar read, cash-flow-classification attribute, close-transition
  audit columns, auto-reversing accruals at close, recurring journal templates, close-checklist gate +
  late-posting date resolver.
- **Phase 4a** — fixed-asset register + straight-line depreciation posting.
- **Phase 4b** — multi-currency posting (engine FxRate, backward-compatible) + period-end unrealized FX
  revaluation (realized-on-settlement still to do — see §1).
- **§7A conversion** — opening-balance journal (balance-sheet + AR/AP open items).
- **Cross-cutting §12** — reversal-of-reversal policy, dimension-required (Job/CostCenter), maker-checker
  large-JE threshold.
- **Phase 5 foundation** — pay-run + payroll journal posting (amounts provided).
- **forge-ui /accounting** — full read UI + interactive period-close and bank-reconciliation (branch
  `feat/accounting-gl-phase1` on forge-ui).

Full InMemory suite **1370 green**. The PG atomicity/concurrency tests still need a Docker box before
un-darking (sandbox has none).

---

## 1. Phase 4b — FX revaluation — **DONE** (commit `7a4b519d`), except realized-on-settlement

- **Multi-currency posting — done.** `PostingRequest.FxRate` (default 1); the engine computes
  `FunctionalAmount = round(TxnAmount × FxRate)`. At `FxRate == 1` it's **byte-for-byte** the old
  single-currency path (no rounding) — all 1370 prior tests stay green. One rate per entry, so the functional
  ledger balances whenever the transaction side does.
- **Period-end unrealized reval — done.** `FxRevaluationService.RevalueAsync(book, currency, newRate, asOf)`
  re-measures the **net foreign monetary position** (cash + AR/AP control in the foreign currency) and posts
  the functional carrying adjustment to `FX_REVALUATION` / `FX_GAIN`|`FX_LOSS` with `AutoReverseNextPeriod`
  (reuses the close auto-reversal). Functional-currency + no-rate-change are no-ops. `POST
  /accounting/fx-revaluation`, gated `CAP-ACCT-FXREVAL`. +5 tests.

**Remaining FX piece — realized FX on settlement.** When a foreign invoice/bill settles at a rate different
from its booking rate, the difference is a *realized* gain/loss. This needs **per-open-item booking-rate
tracking** (the AR/AP sub-ledger is currently a control-balance projection, not strict open items), then a
hook in the payment/settlement posting services: relieve AR/AP at the booked functional carrying value, take
cash at the settlement rate, and post the difference to `FX_GAIN`/`FX_LOSS`. Build alongside an open-item
sub-ledger load (it pairs naturally with §7A's open-item conversion). A future `CTA` line handles equity
translation for consolidations.

## 2. Cross-cutting §12 — JE attachments — **supported by existing infra**

`FileAttachment` is polymorphic and the generic `FilesController` already exposes
`POST /files/{entityType}/{entityId}/files` for any entity type — so a manual JE takes attachments today via
`EntityType = "JournalEntry"`. The UI/JE detail can surface it directly; no new backend. **Caveat:** the route
+ `FileAttachment.EntityId` are `int` while `JournalEntry.Id` is `long`; fine until a book exceeds ~2.1B
journal entries — widen to `long` (or key by entry number) before that's a concern.

## 3. Pre-go-live (independent of new features)

- Run the deferred **Postgres atomicity + concurrency tests** on a Docker-enabled box (the `…AtomicityTests`
  and the 3-way-match FOR UPDATE race) before flipping `CAP-ACCT-FULLGL` on.
- Seed the new capability codes into the catalog: `CAP-ACCT-DEPRECIATION`, `CAP-PAYROLL-RUN`,
  `CAP-ACCT-MULTICURRENCY`, `CAP-ACCT-FXREVAL` (and ratify the deferred `CAP-P2P-BILL`/`CAP-P2P-PAY`).
- Maker-checker is the synchronous first cut (approver supplied at post time, must differ from poster); a full
  async pending-approval workflow is the follow-up.
- Operational inventory movements (material issue→WIP, job-complete→FG, ship relief) remain **unwired** while
  Armory Plastics tests — they feed STAGE-E valuation decrements + COGS relief; wire when that hold lifts.
