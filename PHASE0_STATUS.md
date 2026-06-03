# Accounting GL — Phase 0 Build Status (for human review)

**Branch:** `feat/accounting-gl-phase0` (forge-api)
**Plan reference:** `/home/daniel-hokanson/dev/armory-works/forge/ACCOUNTING_SUITE_PLAN.md` §5
**Date:** 2026-06-03
**Milestone type:** Internal engineering milestone (per §1 / §10), **not** a customer increment.
**Capability state:** `CAP-ACCT-FULLGL` remains **OFF / unwired** — the code is dark. Services are
registered in DI and reachable only via `IPostingEngine` + read/config interfaces, but no operational
command site calls the engine, so nothing posts or reads the `acct_*` tables at runtime.

---

## 1. Build status — GREEN

- `dotnet build forge.api/forge.api.csproj` → **Build succeeded, 0 Warning(s), 0 Error(s)** (transitively
  builds forge.core, forge.data, forge.integrations).
- `dotnet ef migrations list` resolves design-time config cleanly; the new migration
  `20260603084548_AddAccountingGlFoundation` is recognized and shows **(Pending)** — i.e. **not applied
  to any database** (we never ran `database update`).

## 2. Unit-test status — GREEN

- `dotnet test forge.tests --filter "FullyQualifiedName~Accounting"` →
  **Passed: 36, Failed: 0, Skipped: 0** (~4 s).
- Of those 36, **24 are new Phase-0 tests**:
  - `Forge.Tests.Accounting.PostingEngineTests` — 20 tests (balanced/unbalanced, Dr/Cr XOR, hard-closed
    reject, soft-closed block + override, no-period, control-line party required/posts, non-postable,
    cross-book, determination-key resolve, unmapped key, non-Manual-without-idempotency-key,
    duplicate-key-returns-existing, reverse + flip, double-reverse, reverse-into-hard-closed, trial
    balance balances + nets reversal).
  - `Forge.Tests.Accounting.LedgerImmutabilityInterceptorTests` — 4 tests (modify posted memo blocked,
    delete posted entry blocked, modify posted line blocked, Posted→Reversed flip allowed).
  - The remaining 12 matched tests are **pre-existing** external-accounting/provider tests
    (`InitiateAccountingOAuthHandlerTests`, `LinkPartToAccountingItemHandlerTests`, etc.) — unrelated to
    Phase 0, also green.

---

## 3. What was created (file inventory)

### Enums — `forge.core/Enums/Accounting/`
- `AccountType.cs` (Asset|Liability|Equity|Income|Expense)
- `NormalBalance.cs` (Debit|Credit)
- `ControlAccountType.cs` (AR|AP|Inventory)
- `FiscalYearStatus.cs` (Open|Closed)
- `FiscalPeriodStatus.cs` (Open|SoftClosed|HardClosed)
- `JournalEntryStatus.cs` (Draft|PendingApproval|Approved|Posted|Reversed)
- `JournalSource.cs` (Manual|AR|AP|Inventory|Payroll|FX|Depreciation|Conversion|System)
- `SubledgerPartyType.cs` (Customer|Vendor)

### Entities — `forge.core/Entities/Accounting/`
- `Book.cs` — `BaseEntity`; Code/Name/FunctionalCurrencyId/ReportingTimeZone(IANA)/RoundingTolerance/IsActive.
- `GlAccount.cs` — `BaseEntity`; AccountNumber/Name/AccountType/NormalBalance/ParentAccountId?/IsControlAccount/ControlType?/IsPostable/IsActive/Description.
- `CostCenter.cs` — `BaseEntity`; BookId/Code/Name/ParentId?/IsActive (self-ref hierarchy).
- `FiscalYear.cs` — `BaseEntity`; BookId/Name/StartDate/EndDate(`DateOnly`)/Status.
- `FiscalPeriod.cs` — `BaseEntity, IConcurrencyVersioned`; FiscalYearId/PeriodNumber/Name/Start/End(`DateOnly`)/Status/Version (close-vs-post race guard).
- `JournalEntry.cs` — **`long` Id, NOT `BaseEntity`** (out of soft-delete filter, not `IConcurrencyVersioned` per §4); BookId/EntryNumber/EntryDate/FiscalPeriodId/FiscalYearId(denormalized)/Source/SourceType+SourceId/IdempotencyKey/CurrencyId/Memo/Status/AutoReverseNextPeriod/ReversalOfEntryId?/ReversedByEntryId?/ApprovedBy?/PostedBy/PostedAt.
- `JournalLine.cs` — **`long` Id, NOT `BaseEntity`**; JournalEntryId/BookId(denormalized)/LineNumber/GlAccountId/JobId?/CostCenterId?/Debit/Credit/CurrencyId/TxnAmount/FunctionalAmount/FxRate/SubledgerPartyType?+SubledgerPartyId?/Description.
- `AccountDeterminationRule.cs` — `BaseEntity`; BookId/Key/GlAccountId + nullable scope (ItemId?/CategoryId?/ValuationClassId?).
- `AcctNumberSequence.cs` — `BaseEntity`; BookId/FiscalYearId/NextValue.
- `LedgerBalance.cs` — `BaseEntity`; grain (BookId, GlAccountId, FiscalPeriodId, CurrencyId) + DebitTotal/CreditTotal.

### Models (DTOs) — `forge.core/Models/Accounting/`
- `PostingRequest.cs` (+ `PostingLine`) — engine input; `AccountKey` XOR `GlAccountId` per line.
- `TrialBalanceRow.cs` (+ `TrialBalance` with `IsBalanced`).
- `PostingException.cs` — carries a machine-readable `Code`.

### Interfaces — `forge.core/Interfaces/`
- `IPostingEngine.cs` — `PostAsync` + `ReverseAsync`.
- `IAccountDeterminationResolver.cs` — `ResolveAsync` (scope args) + `ValidateKeysAsync` (seed/startup).
- `IAcctNumberSequenceAllocator.cs` — `AllocateNextAsync`.
- `ITrialBalanceService.cs` — `GetTrialBalanceAsync`.

### EF configurations — `forge.data/Configuration/Accounting/`
- `BookConfiguration.cs`, `GlAccountConfiguration.cs`, `CostCenterConfiguration.cs`,
  `FiscalYearConfiguration.cs`, `FiscalPeriodConfiguration.cs`, `JournalEntryConfiguration.cs`,
  `JournalLineConfiguration.cs`, `AccountDeterminationRuleConfiguration.cs`,
  `AcctNumberSequenceConfiguration.cs`, `LedgerBalanceConfiguration.cs`.
- All tables `acct_*` snake_case; **explicit short FK/index names** via `HasConstraintName`/
  `HasDatabaseName` (63-char Npgsql truncation guard, §5.6); money `(18,4)`, FxRate `(18,8)`; all
  `JournalLine` FKs `DeleteBehavior.Restrict`; JE→line never cascade; unique `(BookId, IdempotencyKey)`
  filtered index; unique `(BookId, FiscalYearId, EntryNumber)`; `JournalLine` CHECK
  `(debit = 0) <> (credit = 0)`; `FiscalPeriod.Version` concurrency token.

### Engine + resolver + reporting — `forge.api/Features/Accounting/`
- `ForgeGlPostingEngine.cs` (`IPostingEngine`) — full §5.2 validation, idempotency
  (duplicate-key-returns-existing), single-currency invariant (FunctionalAmount=TxnAmount, FxRate=1),
  inline `LedgerBalance` maintenance, `ReverseAsync` with Posted→Reversed carve-out.
- `AccountDeterminationResolver.cs` (`IAccountDeterminationResolver`) — `(BookId, Key[, scope])`,
  most-specific-scope-wins, no cross-book fallback.
- `TrialBalanceService.cs` (`ITrialBalanceService`) — filter-immune (`IgnoreQueryFilters`), sums
  functional amounts of Posted+Reversed (reversal nets to zero), Phase-0 sums **raw lines** for provable
  correctness.

### Interceptor — `forge.data/Interceptors/`
- `LedgerImmutabilityInterceptor.cs` — `SaveChanges` interceptor blocking Modified/Deleted on Posted
  ledger rows, with the single Posted→Reversed (+ ReversedByEntryId) carve-out.

### Sequence allocator — `forge.data/Repositories/`
- `AcctNumberSequenceAllocator.cs` (`IAcctNumberSequenceAllocator`) — atomic
  `INSERT … ON CONFLICT DO UPDATE … RETURNING` row-locked counter (the safe `JobRepository` pattern).

### Seed — `forge.api/Data/`
- `SeedData.Accounting.cs` (new partial) — `SeedAccountingAsync`: default `Book` (functional currency =
  base USD, created if missing), 28-account small-manufacturer CoA incl. control accounts, one
  `AccountDeterminationRule` per seeded key, current `FiscalYear` + 12 monthly periods, and the
  `(book, year)` EntryNumber counter. **Idempotent** (run-once guard on `acct_books` empty).
- `SeedData.cs` (modified) — calls `await SeedAccountingAsync(db);` from the main seed path.

### Migration — `forge.data/Migrations/`
- `20260603084548_AddAccountingGlFoundation.cs` + `.Designer.cs` (EF-scaffolded, never hand-authored).
- `AppDbContextModelSnapshot.cs` (modified) — EF-regenerated to include the `acct_*` model.

### DI / context wiring
- `forge.api/Program.cs` (modified) — registers `IAccountDeterminationResolver`,
  `IAcctNumberSequenceAllocator`, `IPostingEngine`, `ITrialBalanceService` (scoped) and the
  `LedgerImmutabilityInterceptor` (singleton, added to `AddDbContext` options).
- `forge.data/Context/AppDbContext.cs` (modified) — 10 `acct_*` `DbSet`s + `OnConfiguring` that
  idempotently self-adds the interceptor (so InMemory unit tests get it without double-registration).

### Tests — `forge.tests/Accounting/`
- `PostingEngineTests.cs` (20 tests), `LedgerImmutabilityInterceptorTests.cs` (4 tests). Use the existing
  `TestDbContextFactory` (InMemory) + a `FakeAllocator` (InMemory can't run the row-lock SQL).

---

## 4. Stubbed / incomplete / uncertain

1. **Postgres `BEFORE UPDATE/DELETE` trigger is NOT implemented (notable deviation).** §2/§4/§5.6/§9 call
   for immutability enforced **two ways**: the `SaveChanges` interceptor (done) **and** a DB-level
   `BEFORE UPDATE/DELETE` trigger (defense in depth, blocks tampering that bypasses EF). The scaffolded
   migration contains no trigger and no raw `migrationBuilder.Sql(...)`. Only the software interceptor
   exists today. The interceptor's own XML doc references "a companion Postgres trigger," but that trigger
   has not been authored. **This should be added (hand-written `migrationBuilder.Sql` in a follow-up
   migration) before Phase 0 is considered DB-complete.**
2. **Migration never applied / not DB-verified.** Per guardrails we did not run `dotnet ef database
   update`. The migration compiles, the snapshot is consistent, and `migrations list` shows it Pending —
   but it has not been executed against Postgres, so the generated DDL is unverified at runtime.
3. **No HTTP/MediatR endpoint for manual journal entries** (§5.5 / start-here checklist #5 mention a
   "manual journal-entry endpoint"). Phase 0 delivers the engine + trial-balance **services**; no
   controller/MediatR command surface is wired (consistent with keeping the capability dark, but it means
   the §5.9 acceptance items are proven only via unit tests, not an API).
4. **Permissions / SoD (§5.7) not implemented.** No `ICurrentUserCapabilities` resolver, no `POST_JE`/
   `APPROVE_JE`/`REVERSE_JE`/`CLOSE_*`/`CONFIGURE_GL` capability checks at the engine boundary, and no
   maker-checker routing for reverse / hard-close / large JEs. `PostAsync`/`ReverseAsync` take a
   server-trusted `userId` int but enforce no capability. **Deferred.**
5. **Audit / observability (§5.8) not wired.** No `ISystemAuditWriter` calls on post/reverse/close/
   determination-rule changes, and no posting-failure alerting. `PostingException` is the surfacing
   mechanism but nothing logs/alerts on it yet. **Deferred.**
6. **Reconciliation sweeper (§4) not implemented** — expected (it has no sources to reconcile until
   Phase 1 wires posting), noted for completeness.
7. **`ForgeGlAccountingService : IAccountingService` provider shim (§5.5) not created.** The provider
   factory does not yet list "forge-native." The GL is reached via `IPostingEngine` as designed; the shim
   is cosmetic for the provider list and is deferred.
8. **`ValidateKeysAsync` exists but is not invoked at seed/startup.** §5.2 wants determination targets
   validated at seed time AND on startup. The resolver method is implemented and unit-reachable, but no
   seed/startup hook calls it yet.
9. **`IClock`/`SystemClock` dependency.** The engine takes `IClock` for `PostedAt`; tests use the real
   `SystemClock`. Assumed already registered in DI (pre-existing). Worth a sanity check at integration.
10. **`AutoReverseNextPeriod` / `AllowSoftClosedOverride`** are persisted/honored at the engine, but the
    consuming flows (period-close auto-reversal; audited override capture) are Phase 3 — flags exist,
    behavior is Phase-0-inert.

---

## 5. Deviations from ACCOUNTING_SUITE_PLAN.md §5

| § | Plan requirement | Status in this build |
|---|---|---|
| §5.1 entities | All 10 entities (`Book…LedgerBalance`), `DateOnly`, `long` ids, denormalized `FiscalYearId` for unique EntryNumber | **Done as specified.** |
| §5.2 engine validations | account resolution, book-consistency, Dr==Cr to 0.00 within tolerance, control-line party, period lock + HardClosed reject + SoftClosed block/override, idempotency (dup→existing), reversal + double-reverse guard | **Done.** Tolerance handling: imbalance > tolerance → `UNBALANCED`; nonzero residual within tolerance → `UNBALANCED_RESIDUAL` (forces an explicit ROUNDING line — slightly stricter than "auto-absorb," matches the "handlers add an explicit ROUNDING line" intent). |
| §5.2 period row lock | `FOR UPDATE` on `FiscalPeriod` | **Done on Npgsql** (`SELECT … FOR UPDATE` + reload). On InMemory the lock is a no-op (test provider can't do `FOR UPDATE`) — acceptable for unit tests; **the concurrent-close race is therefore NOT covered by an automated test** (§5.9 lists "incl. under a concurrent close"). |
| §5.2 immutability | interceptor **+** Postgres trigger, with Posted→Reversed carve-out | **Interceptor done; Postgres trigger MISSING** (see §4.1 above). This is the principal deviation. |
| §5.3 trial balance | filter-immune, functional amounts, Posted incl., nets Reversed | **Done.** Reads **raw lines** in Phase 0 (provable); `LedgerBalance` is maintained but the TB does not yet read from it (plan says the incremental read path is wired in Phase 1 — consistent). |
| §5.4 seed | Book, small-mfg CoA w/ control accounts, determination rules, FY + 12 periods | **Done** (28 accounts; superset of determination keys seeded). Accountant review of CoA/keys (§8.2) is an open ratify-item, not done here. |
| §5.5 capability/provider wiring | implement `CAP-ACCT-FULLGL` gate; opening-balances hard-gate; `ForgeGlAccountingService` shim | **Deferred** (capability intentionally left OFF/dark per task guardrails; no gate, no hard-gate, no provider shim yet). |
| §5.6 EF/Postgres | snake_case, explicit short names, Restrict, precision, non-auditable base, no hand-edit snapshot | **Done.** |
| §5.7 permissions/SoD | capability-based enforcement at engine boundary, maker-checker | **Not implemented (deferred).** |
| §5.8 audit/observability | audit writes + failure alerting | **Not implemented (deferred).** |
| §5.9 acceptance | proven via tests | **Mostly proven by unit tests**, except: (a) concurrent-close race not auto-tested, (b) permissions not enforced/tested, (c) audit not written/tested, (d) no API endpoint. |

**Summary:** the **GL engine core (entities, configs, posting/reversal validation, idempotency,
immutability interceptor, determination resolver, sequence allocator, trial balance, seed, migration) is
complete and green.** The **cross-cutting concerns layered on top of the engine — the Postgres
immutability trigger, capability gating, SoD/permissions, audit/alerting, the manual-JE API, and the
provider shim — are deferred / not yet built.** This matches "engine in isolation" but falls short of the
full §5 surface; the trigger gap (§4.1) is the one item that is arguably in-scope for "the engine in
isolation" and should be closed next.

---

## 6. Exact commands to build / test / run-migration

All dotnet commands require this environment prefix (per project memory + guardrails):

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export Jwt__Key="design_time_only_dummy_key_at_least_32_chars_000"
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=forge;Username=postgres;Password=postgres"
```

Run from the repo root `/home/daniel-hokanson/dev/armory-works/forge/forge-api`.

**Build (whole API + dependencies):**
```bash
dotnet build forge.api/forge.api.csproj
# or the whole solution:
dotnet build forge.slnx
```

**Run the Phase-0 unit tests:**
```bash
dotnet test forge.tests/forge.tests.csproj --filter "FullyQualifiedName~Forge.Tests.Accounting"
# (broader "~Accounting" also passes but pulls in pre-existing provider tests)
```

**Inspect the migration (does NOT touch the DB):**
```bash
dotnet ef migrations list -p forge.data --startup-project forge.api
# shows 20260603084548_AddAccountingGlFoundation (Pending)
```

**Apply the migration — RUN ONLY ON A HUMAN-REVIEWED, NON-PROD DB (this autonomous run did NOT do this):**
```bash
dotnet ef database update -p forge.data --startup-project forge.api
```
> ⚠️ There is also a **separate pre-existing pending migration**
> `20260602065040_AddManualOverrideReasonToPurchaseOrderLine` that `database update` would apply first.
> Review both before running. Add the **Postgres immutability trigger** (§4.1) before relying on the
> schema for tamper-evidence.

---

*Generated for human review of the autonomous Phase-0 build. The capability `CAP-ACCT-FULLGL` remains
OFF; all Phase-0 code is dark and reached only behind the engine/read interfaces.*
