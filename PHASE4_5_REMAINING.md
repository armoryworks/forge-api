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
- **§7A conversion** — opening-balance journal (balance-sheet + AR/AP open items).
- **Cross-cutting §12** — reversal-of-reversal policy, dimension-required (Job/CostCenter), maker-checker
  large-JE threshold.
- **Phase 5 foundation** — pay-run + payroll journal posting (amounts provided).
- **forge-ui /accounting** — full read UI + interactive period-close and bank-reconciliation (branch
  `feat/accounting-gl-phase1` on forge-ui).

Full InMemory suite **1370 green**. The PG atomicity/concurrency tests still need a Docker box before
un-darking (sandbox has none).

---

## 1. Phase 4b — FX revaluation (the one genuinely-large remaining build)

**Why it's not done in this run:** realized/unrealized FX requires real foreign-currency entries, but the
engine pins the **Phase-0 single-currency invariant** — every line's `CurrencyId` = the entry's,
`FunctionalAmount = TxnAmount`, `FxRate = 1` (`ForgeGlPostingEngine` ~line 151–154). Un-pinning that is a
cross-cutting change to the engine + every posting service, and **all 1370 tests assume FxRate=1**. Rushing
it risks the whole ledger. It needs its own focused, well-tested workstream — not a marathon-tail rush.

**Concrete plan (do this as a dedicated stage):**
1. **Multi-currency posting (prerequisite).** Let a `PostingRequest`/line carry a transaction currency ≠ the
   book functional currency + an `FxRate`; the engine computes `FunctionalAmount = round(TxnAmount × FxRate)`
   and **balances in FUNCTIONAL** (the txn side won't net to zero across currencies). Add a tolerance line for
   functional rounding. Gate behind a `CAP-ACCT-MULTICURRENCY` so the single-currency path is unchanged when
   off. An `FxRateService` (rate source: manual table + optional provider) supplies the rate by (currency, date).
2. **Realized FX on settlement** (Phase-1-onward, AR/AP cash): when a foreign invoice/bill settles at a
   different rate than booked, post the difference to `FX_GAIN` / `FX_LOSS`. Hook in the existing
   payment/settlement posting services (they already compute the AP/AR relief).
3. **Period-end unrealized reval:** an `FxRevaluationService` that, as of a date, revalues open foreign AR/AP/
   cash balances to the period-end rate and posts the unrealized gain/loss with `AutoReverseNextPeriod = true`
   (reuses the close auto-reversal already built) + a `CTA` line for equity translation. Gate `CAP-ACCT-FXREVAL`.
4. Tests: multi-currency balanced post; realized gain + loss on settlement; unrealized reval posts +
   auto-reverses next period; reval reconciles to the FX-adjusted control balances.

## 2. Cross-cutting §12 — JE attachments (small)

`FileAttachment` is already polymorphic (`EntityType` + `EntityId`). Manual-JE attachments are mostly a wiring
step: allow `EntityType = "JournalEntry"` through the existing attachment API + surface it on the JE detail.
**Caveat:** `FileAttachment.EntityId` is `int` while `JournalEntry.Id` is `long` — either widen the attachment
key for JEs or store the JE number. Decide before wiring.

## 3. Pre-go-live (independent of new features)

- Run the deferred **Postgres atomicity + concurrency tests** on a Docker-enabled box (the `…AtomicityTests`
  and the 3-way-match FOR UPDATE race) before flipping `CAP-ACCT-FULLGL` on.
- Seed the new capability codes into the catalog: `CAP-ACCT-DEPRECIATION`, `CAP-PAYROLL-RUN`,
  `CAP-ACCT-MULTICURRENCY`, `CAP-ACCT-FXREVAL` (and ratify the deferred `CAP-P2P-BILL`/`CAP-P2P-PAY`).
- Maker-checker is the synchronous first cut (approver supplied at post time, must differ from poster); a full
  async pending-approval workflow is the follow-up.
- Operational inventory movements (material issue→WIP, job-complete→FG, ship relief) remain **unwired** while
  Armory Plastics tests — they feed STAGE-E valuation decrements + COGS relief; wire when that hold lifts.
