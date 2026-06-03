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

## 7. Completion pass (2026-06-03)

A second autonomous pass closed most of the §4 "stubbed / deferred" items above. **The capability
`CAP-ACCT-FULLGL` remains OFF and the engine stays dark** — nothing was wired to an operational command
site (Invoice/PO/Job/etc.). The engine is now reachable at runtime **only** via the new
capability-gated GL endpoints (which 403 at the edge while FULLGL is off) and the test suite.

### 7.1 Build + full-suite test — GREEN
- `dotnet build forge.slnx` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test forge.tests` (**whole suite, not just Accounting**) →
  **Passed: 1165, Failed: 0, Skipped: 8** (~34 s). The 8 skips are pre-existing `Remediation.*`
  placeholders, unrelated to accounting.
- Accounting tests now total **69** (was 24 at `f70e10ba`) — **+45 new** across this pass.
- `dotnet ef migrations list` still resolves design-time config cleanly; the new migration
  `20260603085539_AddLedgerImmutabilityTriggers` shows **(Pending)** alongside the foundation migration —
  **neither was applied** (`database update` never run, per guardrails).

### 7.2 What this pass ADDED

**(a) Postgres immutability triggers — closes the §4.1 principal gap.**
- `forge.data/Migrations/20260603085539_AddLedgerImmutabilityTriggers.cs` (+ `.Designer.cs`) —
  a new migration of **hand-written `migrationBuilder.Sql`** only (the foundation migration's
  EF-scaffolded snapshot was NOT hand-edited). It creates `BEFORE UPDATE OR DELETE` triggers +
  trigger functions on `acct_journal_entries` and `acct_journal_lines` that `RAISE EXCEPTION`
  (`ERRCODE = restrict_violation`) on any mutation of a `Posted`/`Reversed` row, with the **same single
  carve-out** the interceptor allows: the `Posted→Reversed` status flip + `reversed_by_entry_id` link on
  a header. Lines follow their header's status. This is the defense-in-depth DB layer the §2/§4/§5.6/§9
  "two ways" immutability requirement called for. (Migration is **Pending** — not yet applied to any DB.)

**(b) Manual journal-entry + trial-balance HTTP endpoints (§5.5 / §5.9 acceptance).**
- `forge.api/Controllers/AccountingGlController.cs` — `POST /api/v1/accounting/journal-entries` and
  `GET /api/v1/accounting/trial-balance`. **Gated dark**: class-level `[RequiresCapability("CAP-ACCT-FULLGL")]`
  + `[Authorize(Roles = "Controller")]`. With FULLGL OFF the capability-gate middleware 403s at the edge
  before the handler runs.
- `forge.api/Features/Accounting/CreateManualJournalEntry.cs` — MediatR command + FluentValidation
  validator + handler. Handler reads the **server-trusted** principal off `IHttpContextAccessor`
  (never client-supplied), builds a `PostingRequest`, and calls `IPostingEngine.PostAsync` — never
  touches `JournalEntry` directly. Command type also carries `[RequiresCapability]` so the MediatR
  `CapabilityGateBehavior` is a second gate.
- `forge.api/Features/Accounting/GetTrialBalance.cs` — MediatR query + handler delegating to
  `ITrialBalanceService`.

**(c) Capability gate / opening-balances hard-gate (§5.5 / §7A).**
- `forge.api/Features/Accounting/GlCapabilityGate.cs` (`IGlCapabilityGate`) — `EvaluateAsync` /
  `AreOpeningBalancesLoadedAsync`: refuses to enable FULLGL for a book until a `Posted`
  `Source=Conversion` opening journal exists (filter-immune via `IgnoreQueryFilters`). **Logic only** —
  intentionally NOT wired into any capability-toggle path (that would be Phase 1), so FULLGL stays OFF.

**(d) Startup determination-map validator (§5.2).**
- `forge.api/Features/Accounting/GlDeterminationStartupValidator.cs` — for each active book, asks
  `IAccountDeterminationResolver.ValidateKeysAsync` that every configured key resolves to a postable,
  active, in-book account. Invoked in `Program.cs` **after** the capability snapshot hydrates: **warns**
  when FULLGL is off (Phase-0 dark) and **fails fast** (throws) when FULLGL is on. Wrapped defensively so
  a validator hiccup never bricks a dark boot.

**(e) Provider shim (§5.5 / §3 gap-4).**
- `forge.integrations/ForgeGlAccountingService.cs` (`IAccountingService`, id `"forge-native"`) — makes the
  native suite **listable/selectable** in `AccountingProviderFactory` (one new row added there). It is a
  **thin shim, NOT the GL seam**: every sync/CRM method throws `NotSupportedException` pointing at
  `IPostingEngine`; only the connectivity probes report a healthy sync-free local provider. Registered in
  DI but does **not** become the active provider.

**(f) Segregation of duties at the engine boundary (§5.7).**
- `forge.core/Enums/Accounting/GlCapability.cs` — `PostJournalEntry / ApproveJournalEntry /
  ReverseJournalEntry / ClosePeriodSoft / ClosePeriodHard / ReopenPeriod / ConfigureGl`.
- `forge.core/Interfaces/ICurrentUserCapabilities.cs` + `forge.core/Interfaces/IGlBoundaryAuthorizer.cs`
  — model-agnostic capability resolver + boundary enforcer contracts.
- `forge.core/Models/Accounting/GlAuthorizationException.cs` — carries the missing `GlCapability`;
  mapped to **403** at the HTTP edge (distinct from `PostingException` → 400).
- `forge.api/Features/Accounting/Sod/CurrentUserCapabilities.cs` — reads effective JWT role claims
  (already template-expanded by `RoleClaimsExpander`); GL capabilities attach to **`Controller`** (bare
  Admin/Manager/OfficeManager get none); exposes the toxic-combination probe (Admin + POST_JE).
- `forge.api/Features/Accounting/Sod/GlBoundaryAuthorizer.cs` — **fail-safe default-deny** (no resolvable
  principal → deny); the toxic-combination is **logged, not blocked** (the solo `OwnerOperator` superuser
  legitimately trips it; the log catches the unintended combos).
- `forge.api/Features/Accounting/ForgeGlPostingEngine.cs` (modified) — `PostAsync`/`ReverseAsync` now call
  the **optional** `IGlBoundaryAuthorizer` (`EnsureAuthorized(PostJournalEntry/ReverseJournalEntry)`).
  The dependency is null-defaulted: the production DI path always supplies a real (default-deny)
  authorizer, while the engine's own unit tests construct it with `null` to exercise posting mechanics
  without an identity context. (CAP-ACCT-FULLGL is OFF, so no command site reaches the engine in prod.)

**(g) HTTP error mapping.**
- `forge.api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — `PostingException` → **400**
  (`problem.code` = machine-readable code); `GlAuthorizationException` → **403**
  (`problem.requiredCapability`). Both reachable only via the gated endpoints.

**(h) DI wiring.**
- `forge.api/Program.cs` (modified) — registers `ICurrentUserCapabilities`, `IGlBoundaryAuthorizer`,
  `IGlCapabilityGate`, `GlDeterminationStartupValidator` (scoped), and invokes the startup validator
  after capability-snapshot hydration. `ForgeGlAccountingService` registered in the provider list.

**(i) New tests (+45).**
- `forge.tests/Accounting/PostingEngineEdgeCaseTests.cs` — additional engine edge cases.
- `forge.tests/Accounting/AccountDeterminationResolverTests.cs` — resolver scope precedence / no
  cross-book fallback / `ValidateKeysAsync`.
- `forge.tests/Accounting/GlSegregationOfDutiesTests.cs` — capability mapping + fail-safe deny + toxic
  combination.
- `forge.tests/Accounting/GlCapabilityGateAndBoundaryTests.cs` — opening-balances hard-gate +
  boundary-authorizer behavior.
- `forge.tests/Accounting/ForgeGlAccountingProviderShimTests.cs` — shim throws on sync methods, probes OK.
- `forge.tests/Handlers/Accounting/AccountingGlHandlerTests.cs` — the MediatR command/query handlers
  (manual-JE post + trial-balance) end to end against the engine.

### 7.3 SoD status (§5.7)
Capability-based SoD is now **implemented and enforced at the engine boundary** (not role-name-based):
GL capabilities attach to `Controller`; the boundary authorizer fail-safe-denies an unresolvable
principal; the toxic-combination (grant-permissions Admin + POST_JE) is **surfaced via a warning log**,
not blocked, so the seeded `OwnerOperator` keeps working. **Maker-checker routing** (Draft→Approved→Posted
for reverse / hard-close / large JEs) is **still deferred** — routine posts go straight to `Posted` as in
Phase 0; the capability keys for it exist but the workflow is Phase 3 (close) / cross-cutting (large-JE
threshold).

### 7.4 Still stubbed / deferred after this pass
1. **Audit / observability (§5.8) — still not wired.** No `ISystemAuditWriter` calls on
   post/reverse/close/determination-rule changes, and no posting-failure / sweeper alerting. The SoD
   toxic-combination warning log is the only observability added. **Deferred.**
2. **Reconciliation sweeper (§4) — not implemented.** Expected — it has no sources to reconcile until
   Phase 1 wires posting.
3. **Maker-checker workflow (§5.7) — deferred** (see §7.3). Routine posts go straight to `Posted`.
4. **Period close / reopen operations — not implemented.** The `ClosePeriodSoft/Hard`, `ReopenPeriod`
   capability keys exist; the close engine + auto-reversal of `AutoReverseNextPeriod` accruals are Phase 3.
5. **Migration not applied / not DB-verified.** Per guardrails we did NOT run `dotnet ef database update`.
   The two new migrations compile and list as Pending; the trigger DDL is therefore **unverified at
   runtime** (the trigger SQL is hand-written and untested against a live Postgres).
6. **Concurrent-close race still not auto-tested.** The `FOR UPDATE` period lock is real on Npgsql but a
   no-op on the InMemory test provider, so §5.9's "incl. under a concurrent close" remains unproven by an
   automated test (would need a Postgres-backed integration test).
7. **Opening-balances hard-gate is logic-only** — `IGlCapabilityGate` is implemented + tested but not
   invoked by any capability-toggle path (intentional; wiring it is Phase 1 and would begin un-darking).

### 7.5 Updated open questions
- **Audit wiring shape:** confirm `ISystemAuditWriter` is the right sink for GL post/reverse/close events
  and define the before/after payload + reason capture (§5.8) before Phase 1 un-darks the engine.
- **Maker-checker large-JE threshold:** still a Book-configurable amount per §5.7 / §12 — needs the value
  + the Draft→Approved→Posted routing decided before reverse/hard-close go live.
- **Determination-validator severity at toggle time:** today severity follows the *startup* FULLGL state.
  When the Phase-1 toggle path flips FULLGL on for a book, it should re-run `GlDeterminationStartupValidator`
  (or `IGlCapabilityGate`) and **fail the toggle** on an unresolved key — confirm that is where the gate
  belongs.
- **Postgres trigger vs interceptor carve-out drift:** the trigger and the C# interceptor each encode the
  `Posted→Reversed` carve-out independently. A live integration test should assert they agree once a DB
  is available (the trigger is currently unverified against Postgres).
- **`accounting_provider = "forge-native"` activation semantics:** selecting the shim as active provider is
  defined as "books live inside Forge"; confirm the Conversion-workstream (§7A) capability flip
  (EXTERNAL off → BUILTIN on → FULLGL on) is the path that sets it, not a bare provider switch.

---

*Generated for human review of the autonomous Phase-0 build + completion pass. The capability
`CAP-ACCT-FULLGL` remains OFF; all Phase-0 code is dark — reachable only behind the engine/read
interfaces and the capability-gated GL endpoints (which 403 at the edge while FULLGL is off).*
