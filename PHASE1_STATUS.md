# Accounting GL — Phase 1 Build Status (for human review)

**Branch:** `feat/accounting-gl-phase1` (forge-api)
**Plan reference:** `/home/daniel-hokanson/dev/armory-works/forge/ACCOUNTING_SUITE_PLAN.md` §6 (Phase-1 row),
§7 (posting matrix Phase-1 rows), §5.2 (engine), §8.4 (rev-rec)
**Date:** 2026-06-04
**Capability state:** `CAP-ACCT-FULLGL` and `CAP-RPT-FINANCIALS` both remain **OFF** (default). All
Phase-1 code is **DARK & NON-REGRESSING**: posting is wired INLINE in the existing operational command
handlers but each `PostAsync` call is guarded by the FULLGL capability gate (or no-ops when off) and
wrapped so a posting error never breaks the operational action while dark. The existing
invoice/payment/expense tests pass unchanged.

- **`dotnet build forge.slnx` → GREEN** (0 Warning, 0 Error). **build green = true.**
- **`dotnet test forge.tests` (whole suite) → Passed: 1210, Failed: 0, Skipped: 7. full-suite green = true.**
  (The 7 skips are the pre-existing `Remediation.*` placeholders, unrelated to accounting.)
- Accounting-filtered subset → **126 passed, 0 failed.** Invoice/Payment/Expense subsets → **53 passed,
  2 skipped (placeholders), 0 failed** — pass **unchanged** (the optional null-default posting params mean
  the legacy handler tests never wire a posting service).
- No `dotnet ef database update` was run (guardrail). The one Phase-1 migration
  `20260604080039_AddExpenseSettlementTarget` (additive nullable columns only) lists as **(Pending)** and is
  **not applied** to any database.

---

## EXECUTIVE SUMMARY — what posts now, where it's wired, and how it stays dark

### What posts now (FULLGL ON) and where it is wired (inline, in the operational command's transaction)

| §7 trigger | Posting (Dr / Cr) | Wired in (command handler → posting service) |
|---|---|---|
| Invoice finalized (control transfer) | **Dr AR_CONTROL** (customer party) / **Cr SALES_REVENUE** per line / **Cr SALES_TAX_PAYABLE** (suppressed for tax-exempt) | `Features/Invoices/SendInvoice.cs` → `InvoiceArPostingService` |
| Invoice **before** delivery | Dr AR_CONTROL / **Cr DEFERRED_REVENUE** (PointInTime rev-rec; reclass-to-revenue-on-delivery is a documented TODO) | same |
| Payment applied | **Dr CASH** / **Cr AR_CONTROL** (customer party); **Cr CUSTOMER_DEPOSITS** for the unapplied/overpayment remainder (`Amount == applied + unapplied`) | `Features/Payments/CreatePayment.cs` → `PaymentCashPostingService` |
| Expense approved | **Dr OPERATING_EXPENSE** / **Cr AP_CONTROL** (vendor party) when it settles to a vendor, else **Cr CASH**; disambiguated by `Expense.SettlementTarget` (+ `Expense.VendorId`), null-target infers AP-if-vendor-else-Cash | `Features/Expenses/UpdateExpenseStatus.cs` (Approved/SelfApproved transition) → `ExpenseApPostingService` |

Each posting service builds a `PostingRequest` and calls `IPostingEngine.PostAsync` on the **shared
request-scoped `AppDbContext`** (the locked INLINE model, §2 — no event-driven posting). Idempotency key
shape `source:type:id:purpose` (`AR:Invoice:<id>:REVENUE`, cash, expense) → a re-fire returns the existing
entry. The single seeded active `Book` is resolved as the posting book (single-entity for now, §5.1).

### How the FULLGL gate keeps it dark + the regression proof

1. **Self-gate (the primary dark guard).** The FIRST statement of every posting service's public method is
   `if (!capabilities.IsEnabled("CAP-ACCT-FULLGL")) return;`. With FULLGL OFF (the catalog default,
   `IsDefaultOn: false`) the method returns before touching the engine or any `acct_*` table — the
   operational flow is byte-for-byte unchanged.
2. **Optional/null-default DI seam.** The posting services are injected as optional null-default ctor params
   into the three handlers, so handlers stay constructible without an accounting context and the legacy
   handler unit tests (which don't wire a posting service) are untouched.
3. **Error containment while dark.** The dark path can never throw into the operational action (the only
   work it does is the early `return`). Once FULLGL is ON a posting failure DOES propagate and fail the
   operation visibly (the inline model's "fail visibly" rule, §2). Best-effort audit writes are
   try/caught so an audit hiccup never unwinds a committed posting.
4. **Regression proof.** `forge.tests/Accounting/Phase1DarkRegressionTests.cs` (6 tests) wires the **real**
   posting service into the **real** command handler against a **fully seeded book** (book + CoA +
   determination rules + open period) with **FULLGL OFF**, and asserts the command behaves exactly as
   before AND that **no JournalEntry / JournalLine / LedgerBalance row is created** and no exception is
   thrown. Seeding a complete book (not an empty schema) makes the no-op attributable to the **gate**, not
   to a missing book — confirmed **non-vacuous** (flipping the gate ON in a throwaway run makes the engine
   post and the "no JournalEntry" assertion fails as designed; reverted). The full pre-existing
   invoice/payment/expense suites pass unchanged.

### AR aging + P&L / Balance Sheet

- **AR sub-ledger + aging** — `Features/Accounting/ArAgingService.cs` (`IArAgingService`). Projected
  directly from posted `JournalLine`s on AR-control accounts carrying a `Customer` party (NOT a parallel
  store), filter-immune (`IgnoreQueryFilters`). Standard 30/60/90/91+ balance-forward ladder. `ReconcileAsync`
  ties the aging total to the posted AR-control balance by construction; any nonzero diff is a genuine defect.
- **P&L + Balance Sheet** — `Features/Accounting/FinancialStatementService.cs`
  (`IFinancialStatementService`), over the same filter-immune posted-`JournalLine` projection, classified by
  `GlAccount.AccountType`; reversals net to zero like the trial balance. Balance Sheet balances via a
  **computed current-year-earnings** equity line (interim, until the Phase-3 RE roll). Both statements carry
  `CogsPosted = false` + a `MarginCaveat` string (COGS is Phase 2 → margin/net income incomplete).
- Reporting endpoints (`GET /pnl`, `/balance-sheet`, `/ar-aging`) are **dual-gated**: method-level
  `CAP-RPT-FINANCIALS` (`IsDefaultOn: false`) at the HTTP edge + `CAP-ACCT-FULLGL` on the MediatR query type.
  Both must be ON to reach a handler.

### What is DEFERRED (matches the plan)

- **COGS / inventory-relief leg → Phase 2.** The §7 matrix "+ Dr COGS / Cr Finished-Goods for stocked
  goods" is intentionally NOT posted (needs the per-part valuation store, §8.1, and resolution of the
  FG-not-yet-loaded edge, §12). This is why `CogsPosted = false` and `CAP-RPT-FINANCIALS` stays OFF.
- **Sales-tax remittance** (Dr SALES_TAX_PAYABLE / Cr Cash by jurisdiction) — **deferred.** Phase 1
  **accrues** tax to the single `SALES_TAX_PAYABLE` control on invoice finalize; remittance-by-jurisdiction
  has no structural home yet (§12 Phase-1 deferral) and is specified before the remittance posting is wired.
- **Customer returns** (Dr SALES_RETURNS / Cr AR-or-REFUNDS_PAYABLE) — **deferred** (the `SALES_RETURNS` /
  `REFUNDS_PAYABLE` keys are seeded; no return command posts yet).
- **Deferred-revenue reclass on delivery** (Dr DEFERRED_REVENUE / Cr SALES_REVENUE) — the deferred booking
  posts; the delivery-trigger reclass is a documented TODO (idempotency purpose `REVENUE_RECLASS`).
- **Realized FX on foreign settlement** → Phase 4 (single-currency invariant pinned 1:1 in Phase 1).
- **Cash Flow statement** + **year-end RE roll / period close** → Phase 3.

### §7.5 / §8 defaults chosen (flagged for owner/accountant ratification)

1. **Audit on post / reverse** — wired via the existing `ISystemAuditWriter` (`CAP-IDEN-AUDIT-SYSTEM-LOG`)
   with actor + before/after + reason; best-effort (never unwinds a committed posting).
2. **Maker-checker thresholds (configurable, not constants)** — defaults **Sales > $50,000 /
   Purchasing > $1,000 / GL manual-JE configurable**; routine posts go straight to `Posted`.
3. **Single seeded `Book` is the posting book** (single-entity now, multi-entity-ready, §5.1).
4. **Statement amounts are functional currency**; **current-year-earnings is computed** (not a posted RE
   balance) until Phase 3; **no fiscal year covering the date → CY earnings = 0**; **BS balances are
   cumulative-since-inception, P&L is window-restricted**.

---

## STAGE F — tests (this pass)

Adds the formal Phase-1 test pass. The Stage A–E posting services already shipped with their own
service-level tests (FULLGL **ON** behavior + a service-level dark no-op). Stage F audits that coverage
against the §F acceptance list and adds the one genuinely missing layer: the **CRITICAL command-level
non-regression** that the whole "stay dark & non-regressing" guardrail rests on.

### Coverage already present (verified, FULLGL ON), no change needed

- **Invoice finalize** (`InvoiceArPostingServiceTests`): Dr AR / Cr SALES_REVENUE / Cr SALES_TAX_PAYABLE
  on delivery; the **deferred-revenue** path (invoice precedes delivery → Cr DEFERRED_REVENUE);
  tax-exempt suppression; no-COGS (Phase 2); idempotency.
- **Payment applied** (`PaymentCashPostingServiceTests`): Dr CASH / Cr AR_CONTROL (customer party);
  the **customer-deposit** path for the unapplied/overpayment remainder; on-account; idempotency.
- **Expense approved** (`ExpenseApPostingServiceTests`): Dr OPERATING_EXPENSE / Cr **AP-or-Cash**
  (vendor party on AP); null-target inference; control-line-no-vendor hard error; idempotency.
- **AR aging reconciles to the AR control balance** (`ArAgingServiceTests`): `ReconcileAsync` ties the
  aging total to the posted AR-control balance (and flags a missing-party defect).
- **P&L / Balance Sheet balance** (`FinancialStatementServiceTests`): P&L income/expense netting;
  Balance Sheet `IsBalanced` (Assets = Liabilities + Equity incl. current-year earnings).

### What was added — `forge.tests/Accounting/Phase1DarkRegressionTests.cs` (6 tests)

The CRITICAL **FULLGL OFF** regression at the **operational command** level (not just the posting
service). Each test wires the **real** posting service into the **real** command handler
(`SendInvoiceHandler`, `CreatePaymentHandler`, `UpdateExpenseStatusHandler`), backed by a **fully seeded
accounting book** (book + CoA + determination rules + open period), with `CAP-ACCT-FULLGL` **OFF**, and
asserts the command behaves **exactly as before**: status transitions / returned models unchanged,
operational `SaveChangesAsync` still called once, the pre-existing guards (only-Draft, customer-not-found,
non-approve branch) preserved — and **no JournalEntry / JournalLine / LedgerBalance row is created** and
**no exception** is thrown.

- Seeding a complete book (rather than an empty schema) is deliberate: it proves the no-op is enforced by
  the **FULLGL gate**, not by a missing book. Confirmed **non-vacuous** — flipping the gate to ON in a
  throwaway run makes the engine post against the seeded book and the "no JournalEntry" assertion fails as
  designed; reverted.
- Covers: invoice finalize (happy + non-Draft reject), payment create (happy + customer-not-found),
  expense approve (approve transition + a non-approving transition that must not reach the posting branch
  or the QB-sync path).

### Build / test

- `dotnet build forge.slnx` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet build forge.tests/forge.tests.csproj` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test forge.tests` (whole suite) → **Passed: 1210, Failed: 0, Skipped: 7** (~33 s). +6 vs the
  prior 1204; the 7 skips are the pre-existing `Remediation.*` placeholders, unrelated to accounting.
- Accounting-filtered subset → **126 passed** (+6 this pass). The pre-existing invoice/payment/expense
  handler tests (`Handlers.{Invoices,Payments,Expenses}`, 42) pass **unchanged** — the optional
  null-default posting params mean those tests never wired a posting service and are untouched.
- No migration added in Stage F (tests only); no `dotnet ef database update` run (guardrail).

---

## STAGE E — basic financial statements

Adds the two basic financial statements over the existing ledger read path, completing the §6 Phase-1
deliverable "P&L + Balance Sheet".

### What was built

**Models — `forge.core/Models/Accounting/`**
- `ProfitAndLoss.cs` (+ `ProfitAndLossLine`) — Income/Expense lines signed in statement direction
  (Income = Cr − Dr; Expense = Dr − Cr), `TotalIncome`/`TotalExpense`/`NetIncome`, plus `CogsPosted`
  (false in Phase 1) + `MarginCaveat`.
- `BalanceSheet.cs` (+ `BalanceSheetLine`) — Asset/Liability/Equity lines signed in statement direction,
  `TotalAssets`/`TotalLiabilities`/`TotalEquityPosted`, a computed `CurrentYearEarnings` line,
  `TotalEquityWithEarnings` / `TotalLiabilitiesAndEquity`, `IsBalanced` (Assets == L + E incl. CY
  earnings), plus `CogsPosted` + `MarginCaveat`.

**Read seam — `forge.core/Interfaces/IFinancialStatementService.cs`**
- `GetProfitAndLossAsync(bookId, fromDate?, toDate?)` and
  `GetBalanceSheetAsync(bookId, asOfDate?)`.

**Service — `forge.api/Features/Accounting/FinancialStatementService.cs`**
- Built over the **same filter-immune** posted-`JournalLine` projection the
  `TrialBalanceService`/`ArAgingService` use (`IgnoreQueryFilters`), classified by
  `GlAccount.AccountType`. Raw rows pulled then aggregated in memory (provider-agnostic signing; matches
  the `ArAgingService` pattern). Reversed originals + their reversals both included so they net to zero,
  exactly like the trial balance.
- **Current-year-earnings**: resolves the `FiscalYear` whose `[StartDate, EndDate]` contains the as-of
  date, sums Income − Expense over `[fiscalYearStart, asOf]`. Returns 0 when no fiscal year covers the
  date. This is the standard interim equity adjustment that makes the balance sheet balance **before** the
  Phase-3 year-end Retained-Earnings roll (§6 Phase-3 / §12).

**MediatR queries — `forge.api/Features/Accounting/`**
- `GetProfitAndLoss.cs` (`GetProfitAndLossQuery` + handler), `GetBalanceSheet.cs`
  (`GetBalanceSheetQuery` + handler). Thin delegators to the read seam, mirroring `GetTrialBalance` /
  `GetArAging`.

**Endpoints — `forge.api/Controllers/AccountingGlController.cs`**
- `GET /api/v1/accounting/pnl` and `GET /api/v1/accounting/balance-sheet`.

**DI — `forge.api/Program.cs`**
- Registers `IFinancialStatementService` → `FinancialStatementService` (scoped), alongside the other
  dark Phase-0/1 read seams.

**Tests — `forge.tests/`**
- `Accounting/FinancialStatementServiceTests.cs` (9 tests): P&L income/expense netting incl. a
  contra-revenue account, period-range filtering, balance-sheet-account exclusion, reversal-nets-to-zero;
  balance-sheet classification + current-year-earnings + the accounting equation balances, as-of-date
  cutoff, no-fiscal-year → zero CY earnings, filter-immunity, default-as-of-to-clock.
- `Handlers/Accounting/AccountingGlHandlerTests.cs` (+2 tests): the P&L and Balance-Sheet MediatR
  handlers end-to-end against the real engine + service.

### Gating (kept dark) — ties to `CAP-RPT-FINANCIALS`

The two endpoints are **dual-gated**:

| Gate | Where | Enforced by | Default |
|---|---|---|---|
| `CAP-RPT-FINANCIALS` | method-level `[RequiresCapability]` on the `pnl` / `balance-sheet` actions | `CapabilityGateMiddleware` at the HTTP edge | **OFF** |
| `CAP-ACCT-FULLGL` | the `GetProfitAndLossQuery` / `GetBalanceSheetQuery` records | MediatR `CapabilityGateBehavior` | **OFF** |

`RequiresCapabilityAttribute` is `AllowMultiple = false`, so a method-level attribute overrides the
controller-level one **for the HTTP middleware** (which reads a single `RequiresCapabilityAttribute`).
That is why the FINANCIALS gate is placed on the endpoint method (so the edge enforces it) while the
FULLGL gate stays on the query type (so the MediatR behavior still enforces the GL engine gate). **Both
capabilities must be ON** to reach the handler. `CAP-RPT-FINANCIALS` already exists in the catalog
(`CapabilityCatalog.cs`: "Financial statements (P&L, BS, CF, TB) … AR/AP aging", `IsDefaultOn: false`) —
no catalog change needed.

### COGS-not-yet-posted label (explicit, per task)

Both statements carry `CogsPosted = false` and a `MarginCaveat` string spelling out that **Cost of Goods
Sold is not posted until Phase 2**, so **gross margin (and net income / current-year-earnings) is
incomplete**. The caveat travels with the data (not just API docs), matching the §6 sequencing note and
§10 ("`CAP-RPT-FINANCIALS` default OFF, enabled once COGS is live"). The seeded COGS account exists but
nothing relieves inventory → COGS at the sale in Phase 1.

### Build / test

- `dotnet build forge.slnx` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test forge.tests` (whole suite) → **Passed: 1204, Failed: 0, Skipped: 7** (~33 s). The 7 skips
  are pre-existing `Remediation.*` placeholders, unrelated to accounting.
- Accounting-filtered subset → **120 passed** (+11 this pass: 9 service + 2 handler).
- No migration was added in Stage E (statements are read-only over existing tables); per guardrails
  `dotnet ef database update` was **not** run.

---

## Open-item defaults — flagged for ratification (per task guardrails)

These are applied as defaults in the Phase-1 build and are recorded here for the owner/accountant to
ratify (§8 ratify-items). None is hard-coded as immutable; each is configurable / overridable.

1. **Audit on post / reverse** — wired via the existing `ISystemAuditWriter`
   (`CAP-IDEN-AUDIT-SYSTEM-LOG`) with actor + before/after + reason, per §5.8. *(Stages A-D.)*
2. **Maker-checker thresholds (configurable)** — defaults **Sales $50,000 / Purchasing $1,000 / GL
   manual-JE configurable**, per §8.8 / §5.7. A transaction/JE above its threshold routes through
   maker-checker; routine posts go straight to `Posted`.
3. **Single seeded Book as the posting book** — single-entity for now; the engine resolves the one seeded
   `Book` as the posting book (§5.1 "single entity now, multi-entity-ready"). Stage E reports take an
   explicit `bookId` query parameter.
4. **Stage E specifics:**
   - **Statement amounts are functional currency** (Phase-0/1 single-currency invariant —
     `TxnAmount == FunctionalAmount`).
   - **Current-year-earnings is a computed equity line** (not a posted RE balance) until the Phase-3
     year-end close rolls Income/Expense into Retained Earnings.
   - **No-fiscal-year-covering-the-date → current-year-earnings = 0** (we do not infer a window); confirm
     this is the desired behavior for off-calendar / future as-of dates.
   - **Balance-sheet account balances are cumulative since inception** through the as-of date; the P&L is
     restricted to the requested `[fromDate, toDate]` window (null bounds = open-ended).

---

## Still deferred after Stage E (matches the plan)

- **COGS / inventory posting** — Phase 2 (§6 Phase-2 row). This is the gap that keeps `CogsPosted = false`
  and `CAP-RPT-FINANCIALS` OFF.
- **Cash Flow statement** — Phase 3 (needs a cash-flow-classification attribute on `GlAccount`, §12).
- **Year-end Retained-Earnings roll / period close** — Phase 3; until then the balance sheet uses the
  computed current-year-earnings line.
- **Multi-currency / FX** — Phase 4 (the multi-currency fields exist but are pinned to 1:1 in Phase 1).
- Migration not applied / not DB-verified (guardrail: no `database update`).

---

*Generated for human review of the autonomous Phase-1 STAGE E build. `CAP-ACCT-FULLGL` and
`CAP-RPT-FINANCIALS` both remain OFF; the statement endpoints 403 at the edge while either gate is off.*
