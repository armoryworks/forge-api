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

---

## STAGE E — basic financial statements (this pass)

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

## STAGE F — posting atomicity (GO-LIVE BLOCKER fix)

**Problem.** Through Stage E the posting was "inline" but **not atomic**: each operational handler called
its own `repo.SaveChangesAsync` *and* the engine's `PostAsync` called a **second** `db.SaveChangesAsync`.
Those were two separate commits. If the operational write committed and the posting then failed (or vice
versa), the database could be left with an operational row and no journal entry (or the reverse) — a
silent ledger integrity hole the moment `CAP-ACCT-FULLGL` is switched on. The engine was *designed* for
"the caller's transaction" (its class doc says so, and its `FOR UPDATE` fiscal-period lock only actually
holds until a wrapping transaction commits) — but no command site ever opened one.

**Fix (this pass).** Each of the three Phase-1 command handlers now wraps its operational change **and**
the inline posting in a single transaction:

- `CreatePayment` — `await using var tx = await db.Database.BeginTransactionAsync(ct); … ; await tx.CommitAsync(ct);`
  (`db` was already injected; the existing optimistic-concurrency `try/catch` is preserved — on a conflict
  it throws and the `await using` rolls the transaction back).
- `SendInvoice` and `UpdateExpenseStatus` — same wrap; `AppDbContext? db` was **added** as an optional
  trailing constructor parameter. In production DI supplies it (a real Npgsql transaction); in the
  isolated mock-based unit tests it is `null` and **no** transaction is opened (behavior is byte-for-byte
  as before — those tests mock the repo and never had a context).

The engine is **unchanged**: when a transaction is already open on the shared context, its
`SaveChangesAsync` *flushes within* that transaction instead of committing, and the handler's single
`CommitAsync` commits operational + ledger together. On a posting exception the `await using` disposes the
transaction → rollback → the operational change is undone too. On the EF InMemory provider
`BeginTransactionAsync` is an ignored no-op (the test factory suppresses `TransactionIgnoredWarning`), so
the dark unit tests are unaffected.

**Verification.**
- `dotnet build` → **0 Warning(s), 0 Error(s)**.
- InMemory suite (whole, minus the 3 Docker-backed Postgres classes) → **1214 passed, 0 failed, 7 skipped**
  (the 7 are pre-existing `Remediation.*` placeholders). **No regression** from the wrap.
- **Real-Postgres rollback/commit proof — `forge.tests/Accounting/Phase1PostingAtomicityTests.cs` (6 tests):**
  for each of the three handlers, a "rolls back" test forces the posting to fail *after* the operational
  write (a deliberately-omitted account-determination rule → `PostingException "DETERMINATION_UNMAPPED"`)
  and asserts, via a **fresh context**, that the operational write did **not** survive and no journal entry
  leaked; payment + invoice "commits" tests prove the happy path persists both. These use the real
  `AppDbContext` over Postgres (the EF InMemory provider *ignores* transactions, so it cannot prove
  rollback) via the shared `PostgresFixture`.

> ⚠️ **Execution gap (must run before enabling FULLGL):** these 6 Postgres tests were **written, compile,
> and follow the repo's Testcontainers convention, but were NOT executed in the authoring session** — that
> sandbox lost Docker daemon access mid-session (uid not in the `docker` group; no passwordless sudo), so
> neither Testcontainers nor a CLI-started container was reachable from the test process. They **must be
> run on a Docker-enabled box before `CAP-ACCT-FULLGL` is turned on.** Two ways:
> 1. **Testcontainers (default):** any environment whose user can reach the Docker socket — `dotnet test --filter "FullyQualifiedName~Phase1PostingAtomicityTests"`.
> 2. **External Postgres (new `FORGE_TEST_PG` escape hatch on `PostgresFixture`):** start a pgvector PG
>    and point the fixture at it — `docker run -d --name pg -e POSTGRES_USER=forge -e POSTGRES_PASSWORD=forgetest -e POSTGRES_DB=forge_test -p 55432:5432 pgvector/pgvector:pg17`,
>    then `FORGE_TEST_PG="Host=localhost;Port=55432;Database=forge_test;Username=forge;Password=forgetest" dotnet test --filter "FullyQualifiedName~Phase1PostingAtomicityTests"`.
>    This same env var lets the existing `SetDefault`/`LeadQueue` Postgres tests run where Testcontainers' Docker.DotNet client is blocked.

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
