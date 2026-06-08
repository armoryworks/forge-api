# Forge API — Project Rules (.NET backend)

> Loaded into every Claude Code session working in the **forge-api** repo. These
> rules override defaults. Follow exactly.
>
> **Why this file exists:** `forge-api` is its own repository. Cloned standalone,
> the umbrella `forge/CLAUDE.md` is NOT in scope — so these backend standards must
> live *here*. This file is the canonical home for the API rules; the umbrella repo
> mirrors/imports them.
>
> **SELF-MAINTENANCE:** when a session introduces a new pattern, entity convention,
> capability, or architectural decision, update this file before the session ends.
>
> **Non-negotiables you will be tempted to skip:** (1) every mutating MediatR
> handler emits an `ActivityLog` row per the Activity-logging rules below; (2) every
> new endpoint reuses or registers a capability (`[RequiresCapability]`); (3) one
> class/interface/enum per file, no "DTO" suffix; (4) soft-delete only; (5) the
> ⚡ Accounting Boundary. Read those sections before writing handlers/entities.


<!-- ===== Critical rules (lint -warnaserror, one-object-per-file, naming, imports, tech stack) ===== -->
## Critical Rules

### Lint discipline (Non-Negotiable)

**No commit may add new lint warnings.** After any UI file edit, run `npm run lint` from `forge-ui` and ensure:

- **Zero errors.** CI fails on errors via `ng lint`'s exit code; never push with a known error.
- **Zero NEW non-spec warnings introduced by this commit.** Pre-existing warnings in unrelated files are acceptable to leave alone (separate cleanup PR), but the diff this commit ships must not add any. The standard `--fix` pass handles the easy ones (autofocus, lifecycle interfaces, stale eslint-disable directives).

A PostToolUse hook in `.claude/settings.json` runs eslint on every UI .ts/.html edit so warnings surface immediately at authoring time rather than at commit. If the hook reports new warnings, fix them before continuing.

For .NET: CI runs `dotnet build --configuration Release -warnaserror`. Compiler warnings break the build. There's no broader analyzer/StyleCop pack wired in today (CLAUDE.md previously claimed both — that was aspirational; only `Nullable enable` + `-warnaserror` are actually configured). Adding a real analyzer pack is a separate effort.

### ONE OBJECT PER FILE (Non-Negotiable)
- **Angular:** One component, service, pipe, directive, guard, interceptor, or model per file. No barrel files (`index.ts`).
- **.NET:** One class, interface, enum, or record per file. Exception: related request/response pair if < 20 lines total.
- **Never mash multiple classes, enums, services, or components into a single file.**

### Naming Conventions

**Angular (TypeScript):**

| Item | Convention | Example |
|------|-----------|---------|
| Files | kebab-case + type suffix | `job-card.component.ts`, `job.service.ts`, `job.model.ts` |
| Classes | PascalCase + type suffix | `JobCardComponent`, `JobService` |
| Variables/properties | camelCase | `jobList`, `isLoading` |
| Observables | camelCase + `$` suffix | `jobs$`, `notifications$` |
| Signals | camelCase, no suffix | `jobs`, `isLoading` |
| Constants | UPPER_SNAKE_CASE | `MAX_FILE_SIZE` |
| Enums | PascalCase name + members | `JobStatus.InProduction` |
| Interfaces | PascalCase, no `I` prefix | `Job`, `Notification` |
| CSS classes | BEM | `job-card__header--active` |
| Control flow | `@if`/`@for` | Never `*ngIf`/`*ngFor` |

**.NET (C#):**

| Item | Convention | Example |
|------|-----------|---------|
| Files | PascalCase | `JobService.cs` |
| Classes/methods/properties | PascalCase | `JobService.GetActiveJobs()` |
| Private fields | _camelCase | `_jobRepository` |
| Parameters/locals | camelCase | `jobId`, `isActive` |
| Interfaces | `I` prefix | `IJobService` |
| Constants | PascalCase | `MaxRetryCount` |
| Namespaces | `Forge.{Project}.{Folder}` | `Forge.Api.Controllers` |
| Models | `*ResponseModel` / `*RequestModel` | **Never "DTO"** |

**Person Names:** When displaying a person's full name, always use `Last, First MI` format (e.g., "Hartman, Daniel J"). This applies everywhere: headers, dropdowns, tables, avatars, reports, PDFs.

**Date Display:** Dates shown to users use `MM/dd/yyyy` (e.g., "03/11/2026"). When time is included, use `MM/dd/yyyy hh:mm` (e.g., "03/11/2026 02:30"). This applies to tables, detail panels, reports, PDFs — all user-facing date rendering.

**Database:** snake_case for tables/columns (auto-converted by EF Core)
**Docker:** services named `forge-*`

### Import Ordering

**TypeScript:** (1) Angular core → (2) Angular Material → (3) Third-party (rxjs, three, etc.) → (4) App shared → (5) Feature-relative. Blank line between groups.

**C#:** (1) System → (2) Microsoft → (3) Third-party (FluentValidation, MediatR, etc.) → (4) Forge

### Tech Stack
- **Frontend:** Angular 21, Angular Material 21, SCSS, standalone components, zoneless (signals)
- **Backend:** .NET 9, MediatR (CQRS), FluentValidation, EF Core + Npgsql
- **Database:** PostgreSQL with `timestamptz` columns (all DateTimes must be UTC)
- **Storage:** MinIO (S3-compatible), **Auth:** ASP.NET Identity + JWT + tiered kiosk auth (RFID/NFC/barcode + PIN) + optional SSO (Google/Microsoft/OIDC) + TOTP MFA
- **Real-time:** SignalR, **Background:** Hangfire, **Mapping:** Mapperly (source-generated), **Logging:** Serilog
- **Date lib:** date-fns (tree-shakeable, official Material adapter)
- **Charts:** ng2-charts (Chart.js), **Dashboard grid:** gridstack, **Tours:** driver.js
- **PDF:** QuestPDF (server), **Barcodes:** bwip-js, **QR:** angularx-qrcode
- **Testing:** Vitest (Angular), xUnit + Bogus (.NET), Cypress (E2E)

---


<!-- ===== Workflow + local CI gates (server: dotnet build -warnaserror && dotnet test) ===== -->
## Branch + PR Workflow

**Pre-beta direct-push (current — flipped 2026-05-07):** Branch protection is off. Push commits directly to `main` on all source repos. The branch + PR ceremony was eating momentum during pre-beta when the only reviewer is Dan and there's no production user pain to protect against. Dropping the ceremony for now; we'll flip it back on once we're approaching beta.

**What this means in practice:**
- Default flow is: edit → run local CI gates → `git commit -m "..."` → `git push origin main`. No branches, no PRs, no auto-merge.
- Commit messages still need to be clear and descriptive — they ARE the change log now.
- Local CI gates are still mandatory (lint, lint:i18n, tests, build for UI; build -warnaserror + tests for server). The whole point of the looser flow is speed, not "skip the gates" — broken main is more disruptive without the PR safety net, not less.
- Group related changes into one commit before pushing. Don't push 5 commits where 1 well-scoped commit captures the change. Squash locally if needed.
- Tag commits with the same prefix style PRs used (`feat:`, `fix:`, `chore:`, `refactor:`, `docs:`) so the log stays scannable.
- For risky changes (schema migrations, breaking API changes, anything Dan flags as "let me look first"), still use a branch + PR. The default is direct, but the option remains for when the change benefits from a separate review.

**When a PR is still useful (judgment call, not required):**
- Schema migrations with non-trivial backfills.
- API changes that break an external integration.
- Anything Dan explicitly says "branch this and let me see it first."
- Spikes / experiments where you might throw the work away.

For these, the model is: branch off main → push → open PR with `--base main` → Dan reviews and squashes when ready. No effort branches, no per-feature stacks.

**Local CI gate commands** (run before every `git push origin main`):

- **UI repo (`forge-ui`):** `npm run lint && npm run lint:i18n && npm run test -- --watch=false`. The `lint:i18n` script (added 2026-05-03) catches the recurring "{key.path} renders raw because en.json is missing it" bug class — `tsc --noEmit`, `ng build`, and `vitest` all silently allow missing keys (vitest specs use a mocked TranslateLoader). When you add a `'foo.bar' | translate` reference, run this before pushing.

  **i18n files live at `forge-ui/public/assets/i18n/{en,es}.json`. NEVER edit `src/assets/i18n/` — that path is intentionally non-existent.** Angular CLI's static-asset directory migrated from `src/assets/` to `public/` and the migrated project kept `public/assets/i18n/` as the only bundled source (per `angular.json`). For ~3 sessions before 2026-05-04, edits went to a phantom `src/assets/i18n/` that wasn't in any build — every new key showed up at runtime as a raw `foo.bar` token while `tsc`, `ng build`, `vitest`, AND the early `lint:i18n` all stayed green. The fix: deleted `src/assets/i18n/` and the lint script now hard-fails if it ever reappears. Don't recreate it. If you need to add a translation, the path is `public/assets/i18n/en.json` (and `es.json`). Server-supplied keys (workflow step labelKeys, validator displayNameKey/missingMessageKey) are scanned by `lint:i18n` from `forge-api/forge.api/Workflows/*.cs` automatically.

  **100% language-parity rule (added 2026-05-05).** Every mapped language file MUST be in 1:1 sync with `en.json` (the canonical source). `lint:i18n` now hard-fails on:
  - Keys present in `en.json` but missing from `es.json` (untranslated).
  - Keys present in `es.json` but missing from `en.json` (orphans).
  - Keys referenced in code but missing from `en.json` (existing rule).

  No "warn-only" lag tolerated — when you add a key to `en.json`, add the matching `es.json` entry in the same commit. When you remove a key from `en.json`, remove the `es.json` entry too. Adding a new mapped language is the same contract: every key in `en.json` must exist in the new file before merge.

- **Server repo (`forge-api`):** `dotnet build --configuration Release -warnaserror && dotnet test`.

Spec tests live under a separate `tsconfig.spec.json` that prod-build doesn't compile, so `tsc --noEmit` and `ng build` alone are not enough — explicit test runs are mandatory.

### Hard rules

- **Don't push broken code to main.** Local CI gates are how we keep main green without the PR safety net. If a gate fails, fix it before pushing.
- **Don't push secrets.** Same as before. Be especially careful when staging — `git add -A` can sweep up `.env` files that the gitignore doesn't catch.
- **Don't force-push to main.** Without branch protection there's no server-side block, but force-push to main destroys other people's history and there's no good reason to do it pre-beta either. If you need to undo a bad commit, push a revert commit.
- **Don't bundle 30 files of unrelated changes into one commit.** The commit message has to honestly summarize what changed; if you can't summarize it in 2 lines, the commit is too big — split it.
- **Older effort/feature branches still in flight (2026-05-07): finish them as PRs.** Don't try to convert mid-flight work to direct pushes. New work starts direct.

---


<!-- ===== Auto-Restart API ===== -->
## Auto-Restart API

**When any .NET backend change is made that requires a restart (controller changes, entity changes, Program.cs, appsettings, etc.), automatically rebuild and restart the API container:**

```bash
docker compose up -d --build forge-api
```

Do not ask the user — just do it after verifying the build passes.


<!-- ===== .NET Patterns ===== -->
## .NET Patterns

### Architecture
- MediatR CQRS: Commands + Queries in `Features/` folder, one handler per file
- FluentValidation: validators alongside handlers (can share file if small)
- Repository pattern: interfaces in `Core/Interfaces/`, implementations in `Data/Repositories/`
- Global exception middleware: `KeyNotFoundException` → 404, `ValidationException` → 400, business exceptions → 409
- Controllers are thin — delegate to MediatR handlers, one controller per aggregate root
- All endpoints `[Authorize]` by default; exceptions: login, register, refresh, health, display
- No `try/catch` in controllers — middleware handles everything
- Problem Details (RFC 7807) for all error responses
- Logging via Serilog: structured, contextual (request ID, user ID, entity ID)

### C# Class Structure
- Interfaces for all services (`IJobService`, `IStorageService`)
- Abstract base classes for shared behavior:
  - `BaseEntity` — `Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `DeletedBy`
  - `BaseAuditableEntity` — extends BaseEntity with `CreatedBy`
- Records for models/value objects — immutable by default
- Composition over deep inheritance — max 2 levels
- Integration pattern: interface + real impl + mock impl (e.g., `IAiService` / `OllamaAiService` / `MockAiService`)
- Entity config: one `IEntityTypeConfiguration<T>` per entity, Fluent API only (no data annotations)

### Database (PostgreSQL + EF Core)
- `AppDbContext` auto-applies:
  - Snake_case naming for all tables/columns/keys/indexes
  - `SetTimestamps()` — auto-sets `CreatedAt`/`UpdatedAt` on `BaseEntity`; auto-stamps `DeletedBy = CurrentUserId.ToString()` whenever a soft delete is committed (`DeletedAt` modified to non-null) and the handler hasn't already set `DeletedBy`. Soft-delete handlers only need to stamp `DeletedAt`; the audit principal follows automatically. Explicit `DeletedBy` values are never overwritten.
  - `NormalizeDateTimes()` — converts `DateTimeKind.Unspecified` to UTC before save
  - Global query filter: `DeletedAt == null` on all `BaseEntity` types
- Soft deletes only — no hard deletes (`DeletedAt` timestamp + `DeletedBy` audit principal — auto-populated by `SetTimestamps()`)
- Fluent API in separate `IEntityTypeConfiguration<T>` classes (no data annotations)
- Foreign key indexes explicit on all FK columns
- `reference_data` table: centralized lookup/dropdown values with `group_id` grouping and immutable `code` field
- Primary keys: `id` (int, auto-increment). Foreign keys: `{table_singular}_id`

### API Conventions
- RESTful: `/api/v1/jobs`, `/api/v1/jobs/{id}`, `/api/v1/jobs/{id}/subtasks`
- Plural nouns for collections; no verbs except RPC-like (`/archive`)
- POST → 201 + Location header; DELETE/PUT no-body → 204
- `IOptions<T>` for config — never raw `IConfiguration` in services
- `MOCK_INTEGRATIONS=true` env var bypasses all external API calls with mock responses

### JSON Serialization
- `JsonStringEnumConverter` — enums serialize as strings
- CamelCase property naming (ASP.NET Core default)

### Pagination
- **Offset-based** for standard lists: `?page=1&pageSize=25&sort=createdAt&order=desc` → response: `{ data, page, pageSize, totalCount, totalPages }`
- **Cursor-based** for real-time feeds (chat, activity, notifications): `?cursor=eyJ...&limit=50`
- Default page size: 25, max: 100
- Client: small datasets (< 100) client-side filter; medium (100-1000) `mat-paginator`; large/unbounded virtual scroll
- `PaginatedDataSource<T>` shared class wraps API pagination

---


<!-- ===== .NET Entity Structure ===== -->
## .NET Entity Structure

### Core Entities (in `forge.core/Entities/`)
```
BaseEntity (Id, CreatedAt, UpdatedAt, DeletedAt, DeletedBy)
├── Job (+ Disposition, DispositionNotes, DisposedAt, ParentJobId, PartId), TrackType, JobStage, JobSubtask, JobActivityLog, JobLink
├── Customer, Contact
├── Part (+ ToolingAssetId FK), BOMEntry (+ LeadTimeDays), Operation, OperationMaterial
├── StorageLocation, BinContent, BinMovement
├── Lead, Expense, Asset (+ tooling fields: CavityCount, ToolLifeExpectancy, CurrentShotCount, IsCustomerOwned, SourceJobId, SourcePartId)
├── TimeEntry, ClockEvent
├── FileAttachment
├── PlanningCycle, PlanningCycleEntry (BaseEntity)
├── Vendor, PurchaseOrder, PurchaseOrderLine (BaseEntity), ReceivingRecord
├── SalesOrder, SalesOrderLine, Quote (Type: Estimate|Quote, SourceEstimateId self-FK), QuoteLine
├── Shipment, ShipmentLine
├── CustomerAddress
├── CompanyLocation (Name, Address, State, IsDefault, IsActive)
├── Invoice, InvoiceLine               ← ⚡ standalone mode
├── Payment, PaymentApplication        ← ⚡ standalone mode
├── PriceList, PriceListEntry
├── RecurringOrder, RecurringOrderLine
├── StatusEntry (polymorphic: EntityType/EntityId, workflow + hold categories)
├── ReferenceData, SystemSetting, SyncQueueEntry
├── PayStub (+ PayStubDeduction), TaxDocument, EmployeeProfile
├── ComplianceFormTemplate, ComplianceFormSubmission, FormDefinitionVersion, IdentityDocument
├── DocumentEmbedding (pgvector vector(384) — RAG index)
├── AiAssistant, ChatMessage, ChatRoom, ChatRoomMember
├── AppNotification, UserNotificationPreference
├── QcTemplate, QcInspection, LotRecord, ProductionRun
├── CustomerReturn, SalesTaxRate, ScheduledTask
├── AuditLogEntry, ActivityLog (polymorphic EntityType/EntityId)
├── UserScanIdentifier, UserPreference
├── Event, EventAttendee
├── TimeCorrectionLog
├── ContactInteraction
├── EdiTradingPartner, EdiTransaction, EdiMapping
├── UserMfaDevice, MfaRecoveryCode
```

### Enums (in `forge.core/Enums/`)
`JobPriority`, `JobLinkType`, `JobDisposition`, `ActivityAction`, `PartType` (legacy — being decomposed into `ProcurementSource` × `InventoryClass` × `ItemKindId`), `ProcurementSource` (Make, Buy, Subcontract, Phantom), `InventoryClass` (Raw, Component, Subassembly, FinishedGood, Consumable, Tool), `TraceabilityType` (None, Lot, Serial — replaces legacy `IsSerialTracked` boolean), `AbcClass` (A, B, C), `PartStatus` (Draft, Prototype, Active, Obsolete), `BOMSourceType` (Make, Buy, Stock), `LocationType`, `BinContentStatus`, `BinMovementReason`, `LeadStatus`, `ExpenseStatus`, `AssetType`, `AssetStatus`, `ClockEventType`, `SyncStatus`, `AccountingDocumentType`, `PlanningCycleStatus`, `PurchaseOrderStatus`, `SalesOrderStatus`, `QuoteType` (Estimate, Quote), `QuoteStatus` (Draft, Sent, Accepted, Declined, Expired, ConvertedToQuote, ConvertedToOrder), `ShipmentStatus`, `InvoiceStatus`, `PaymentMethod`, `CreditTerms`, `AddressType`, `EventType` (Meeting, Training, Safety, Other), `AttendeeStatus` (Invited, Accepted, Declined, Attended), `InteractionType` (Call, Email, Meeting, Note), `EdiFormat`, `EdiTransportMethod`, `EdiDirection`, `EdiTransactionStatus`, `MfaDeviceType`

---


<!-- ===== SignalR Conventions ===== -->
## SignalR Conventions
- One hub per domain: `BoardHub`, `NotificationHub`, `TimerHub`, `ChatHub`
- Method naming: PascalCase server-side, camelCase client-side
- Groups: subscribe by entity — `job:{id}`, `sprint:{id}`, `user:{id}`
- Angular service handles auto-reconnect with exponential backoff
- Optimistic UI: card moves update locally immediately, server confirms/rolls back via SignalR
- Connection state exposed as signal — UI shows "reconnecting..." banner when disconnected

---


<!-- ===== IClock Abstraction ===== -->
## IClock Abstraction

Injectable clock for testable time-dependent code. Production uses `SystemClock` (wraps `DateTime.UtcNow`), E2E simulation uses `SimulationClock` (controllable time).

```csharp
// Inject in handlers/services:
private readonly IClock _clock;

// Use instead of DateTime.UtcNow:
var now = _clock.UtcNow;
```

Registered in `Program.cs`. Used by `AppDbContext.SetTimestamps()` and time-dependent handlers.

---


<!-- ===== Key Functional Decisions (incl. NON-NEGOTIABLE activity-logging rules) ===== -->
## Key Functional Decisions

### Kanban Board
- Track types: Production, R&D/Tooling, Maintenance, Other + custom
- Cards move backward unless QB document at that stage is irreversible (Invoice, Payment)
- Multi-select: `Ctrl+Click`, bulk actions (Move, Assign, Priority, Archive)
- SignalR real-time sync, last-write-wins, optimistic UI
- Cards archived (never deleted)
- Column body: white background (`--surface`) with 2px inset border matching stage color via `--col-tint` CSS custom property
- **IsShopFloor filter**: boolean on `TrackType` + `JobStage` — controls which stages appear on the shop floor display (physical-work stages only)

### Shop Floor Display
- Full-screen kiosk at `/display/shop-floor` with RFID/barcode scan → PIN auth flow
- **Worker card grid**: 5-column, square cards with horizontal layout, left status stripe matching stage color
- **Job actions**: timer start/stop, Mark Complete overlay
- **Auto-dismiss timeouts**: PIN phase (20s), job-select phase (15s)
- **Theme/font persistence**: saved to localStorage for kiosk continuity
- **IsShopFloor filter**: only shows jobs in stages where `IsShopFloor = true`

### Production Track Stages (QB-aligned)
Quote Requested → Quoted (Estimate) → Order Confirmed (Sales Order) → Materials Ordered (PO) → Materials Received → In Production → QC/Review → Shipped (Invoice) → Invoiced/Sent → Payment Received (Payment)

### Planning Cycles
- Default 2 weeks (configurable). Day 1 = Planning Day with guided flow
- Split-panel: backlog (left) → planning cycle (right), drag to commit
- Daily prompts: Top 3 for tomorrow each evening
- End of cycle: incomplete items roll over or return to backlog

### Activity Log
- Per-entity chronological timeline (job, part, asset, lead, customer, expense)
- Batch field changes collapse into expandable entries
- Inline comments with @mentions → notification
- Filterable by action type and user. Immutable entries.

#### Activity logging rules (Non-Negotiable)

Every MediatR command handler that mutates a tracked entity MUST emit at least one `ActivityLog` row before its `SaveChangesAsync`. The Activity tab is the audit trail; missing rows = silent state changes.

1. **Definitional vs transactional split** — this gates everything else below.
   - **Definitional / master-data entities** describe what something *is* and are read by downstream transactions: `Part`, `Vendor`, `Customer`, `Contact`, `Asset`, `BOMEntry`, `VendorPart` (and its price tiers), `PriceList`, `RecurringOrder`, `QcTemplate`, `ComplianceFormTemplate`, `WorkflowDefinition`, `ReferenceData`, `SystemSetting`. Mutations here get the indexing-points treatment (rule 2).
   - **Transactional / event-stream entities** *happen* — they're discrete records of operations: `Job`, `SalesOrder`, `Quote/Estimate`, `Invoice`, `Shipment`, `Payment`, `PurchaseOrder`, `BinMovement`, `TimeEntry`, `ClockEvent`, `ContactInteraction`, `Notification`, `ChatMessage`, plus the line-items of all of those. Mutations here log ONLY on the entity itself (and its parent header where applicable — e.g. a SalesOrderLine mutation logs on the SO, not on the upstream Part). Pushing transactional events onto the master-data Activity tab turns it into a transaction log and drowns the definitional changes that actually matter for audit.
   - When in doubt, ask: "If I'm looking at this Part / Vendor / Customer next year, is this row about *what changed in the definition* (yes → log it here) or *something operational that happened to use this definition* (no → log on the transactional entity, not here)?"
   - **Self-auditing-data exception.** Some definitional collections *are* their own audit trail — the current state, viewed in its native UI, fully describes what the entity is. `BOMEntry` is the canonical example: the BOM tab IS the history of "what this part is composed of"; an activity-feed row saying "added component X" duplicates information already trivially visible. For these, skip the activity log entirely on the collection mutations themselves — only log the *parent* entity's own definitional changes (rename, status change, etc.). Apply this exception conservatively: it's earned only when the collection is small, fully visible in one place, and a simple "added/removed/changed" verb adds nothing the screen doesn't already show. Fields with semantics (price, lead time, vendor preference) do NOT qualify — those go through the indexing-points rule.

2. **Indexing-points rule (definitional entities only).** When the mutated entity sits at an indexing point between multiple definitional entities (e.g. `VendorPart` bridges Part ↔ Vendor; `BOMEntry` bridges parent-Part ↔ component-Part; `Contact` bridges to Customer), emit a row for **every** involved entity — not just the one the user is currently viewing. Use `db.LogActivityAt(action, description, ("Part", partId), ("Vendor", vendorId))` from `Forge.Data.Extensions.ActivityLogExtensions`. Order doesn't matter; the helper writes one row per pair.

3. **Rollup rule.** A multi-field update produces ONE activity row whose `Description` summarizes all changed fields (e.g. `"Updated 4 fields: leadTimeDays, minOrderQty, packSize, notes"`). Do NOT emit one row per field — per-field history is the History tab's stream (a different table / different concept). For UpdateXxxHandlers, build a `List<string> changedFields` while applying patches, then write one row referencing them all.

4. **Action verb conventions.** Use kebab-case domain verbs: `created`, `updated`, `deleted`, `archived`, plus specific verbs like `vendor-source-added`, `vendor-source-removed`, `price-tier-added`, `price-tier-updated`, `price-tier-removed`, `preferred-vendor-changed`. Verbs are queryable — don't free-form them.

5. **Description format.** First clause = what changed (in human terms), second clause (optional) = the defining identifiers ("qty ≥ 100 @ $1.50 USD effective 2026-05-04"). The History tab parses on FieldName/OldValue/NewValue; the Activity tab renders Description verbatim. Keep it under ~120 chars.

6. **No cancellation token on the helper.** `LogActivityAt` doesn't take a CT — it just adds to the change-tracker; the surrounding `SaveChangesAsync(ct)` is what flushes.

7. **Helper handles current user.** `LogActivityAt` reads `AppDbContext.CurrentUserId` (set by middleware) — handlers do NOT need to inject `IHttpContextAccessor` or pass user IDs. If `CurrentUserId` is null (system-initiated operation, e.g. Hangfire job), the row is logged with `UserId = null` and renders as "System" in the UI.

### Reference Data
- Single `reference_data` table for all lookups (expense categories, lead sources, priorities, statuses, etc.)
- Recursive grouping via `group_id`. `code` immutable, `label` admin-editable. `metadata` JSONB.
- One admin screen manages everything — no scattered lookup tables

### Company Profile & Locations
- Company profile stored as `company.*` system settings (name, phone, email, EIN, website)
- `CompanyLocation` entity — multiple locations per install, exactly one default (filtered unique index)
- Per-employee `WorkLocationId` FK on `ApplicationUser` — determines state withholding; null = default location
- Setup wizard: 2-step (admin account → company details + primary location)
- Admin settings tab: Company Profile form, Locations DataTable (CRUD + set-default), CompanyLocationDialogComponent

### User Preferences
- Centralized `user_preferences` table, key-value: `table:{id}`, `theme:mode`, `sidebar:collapsed`, `dashboard:layout`
- `UserPreferencesService` loads on init, caches in memory, debounced PATCH on change
- Restored on login from any device

---


<!-- ===== Accounting Boundary (Critical) ===== -->
## ⚡ Accounting Boundary (Critical)

Some features duplicate functionality that an accounting system (QuickBooks, Xero, etc.) handles natively. These features must be **cordoned off** so they only activate in standalone mode (no accounting provider connected). See `docs/qb-integration.md` for the authoritative boundary definition.

### Rules for Accounting-Bounded Code

1. **Every accounting-bounded feature must check `IAccountingService.IsConfigured` (.NET) or `AccountingService.isStandalone` (Angular).** When a provider is connected, the feature becomes read-only or hidden.

2. **Mark all accounting-bounded specs with `⚡ ACCOUNTING BOUNDARY`** in functional-decisions.md and other docs so they are easily searchable.

3. **Accounting-bounded features** (standalone mode only):
   - Invoices (local CRUD, PDF generation)
   - Payments (local recording, application to invoices)
   - AR Aging (computed from local invoices/payments)
   - Customer Statements (generated from local data)
   - Sales Tax tracking (simple per-customer rate)
   - Financial Reports (P&L, revenue, payment history)
   - Vendor management (full local CRUD — read-only when integrated)
   - Credit terms management

4. **Never-in-app features** (regardless of mode):
   - General ledger / bookkeeping
   - Payroll tax calculations
   - Bank reconciliation
   - Check writing
   - Depreciation schedules
   - Full accrual-basis accounting

5. **Always-in-app features** (regardless of mode):
   - Sales Orders, Quotes, Shipments
   - Price Lists, Quantity Breaks, Recurring Orders
   - Customer Addresses (multi-address model)
   - Margin calculations (estimated from app-owned data)

6. **Codified via Phase 4 capability gating.** The accounting boundary is now enforced through the capability system as the mutex pair `CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN` (the only declared mutex in the catalog). `CAP-ACCT-FULLGL` is registered as an aspirational placeholder — never enabled, gating returns 403 with a "not yet available" tone. See the **Capability Gating** section below for the mechanism.

### Implementation Pattern
```csharp
// .NET — Controller or handler checks mode
if (_accountingService.IsConfigured)
    return StatusCode(409, "Feature disabled — managed by accounting provider");

// Angular — Component hides/shows based on mode
readonly isStandalone = this.accountingService.isStandalone;
// Template: @if (isStandalone()) { <invoice-crud /> }
```

---


<!-- ===== Capability Gating ===== -->
## Capability Gating (Phase 4)

The system runs on a **per-install capability gate**: 152 named capabilities (e.g., `CAP-MD-CUSTOMERS`, `CAP-INV-LOTS`, `CAP-EXT-AI-ASSISTANT`) are registered in a static catalog. Each install's capability state is stored in the `capabilities` table; controllers and Hangfire-fired commands carry `[RequiresCapability("CAP-...")]` attributes; the `CapabilityGateMiddleware` (controller side) and `CapabilityGateBehavior` (MediatR side) short-circuit with 403 + envelope when a capability is disabled. Bootstrap-exempt endpoints (auth, descriptor, capability admin) carry `[CapabilityBootstrap]` instead so admins are never locked out.

**Where things live:**
- **Catalog (source of truth)**: `forge-api/forge.api/Capabilities/CapabilityCatalog.cs` — 152 capabilities with code, name, area, default-state, dependencies/mutexes
- **Relations**: `CapabilityCatalogRelations.cs` — dependency edges + mutex pairs (only one declared mutex today: `CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN`)
- **Snapshot + middleware**: `CapabilitySnapshot.cs`, `ICapabilitySnapshotProvider`, `CapabilityGateMiddleware.cs`, `CapabilityGateBehavior.cs`
- **Mutation API**: `CapabilitiesController` exposes `PUT /api/v1/capabilities/{code}/enabled`, bulk-toggle, validate, audit-log; preset & discovery endpoints layered on top
- **Frontend service**: `CapabilityService` (loaded on login, refreshes on SignalR `capabilityChanged` push) + `*appCap` directive + `capabilityGuard` route guard

**Toggling capabilities:**
- Admin UI at `/admin/capabilities` (browse grid grouped by area), `/admin/capabilities/:code` (detail), `/admin/capabilities/audit-log` (history)
- Discovery wizard at `/admin/discovery` (22-question flow, server-side recommendation engine, applies a preset)
- Preset browser at `/admin/presets` (8 presets — 7 named + Custom — with diff modal before apply)
- Direct API: `PUT /api/v1/capabilities/{code}/enabled` (admin-only, bootstrap-exempt)

**Adding a new feature**: see `docs/coding-standards.md` §0 — every new endpoint either reuses an existing capability or registers a new one in the catalog before it ships.

**Design artifacts (deep-dive, decision history)**:
- `phase-4-output/4A-capability-catalog/` — all 129 capabilities with rationale (Phase 4 snapshot; catalog is 152 today)
- `phase-4-output/4B-preset-design/` — 8 presets with target profile + capability set
- `phase-4-output/4C-discovery-flow/` — 22-question wizard + recommendation algorithm
- `phase-4-output/4D-gating-mechanism/` — middleware + descriptor + audit pipeline
- `phase-4-output/4E-admin-ui/` — browse / discovery / preset / detail screens
- `phase-4-output/4F-implementation-plan/` — phasing strategy + per-phase decisions
- `phase-4-output/PHASE-4-CLOSEOUT.md` — rollup summary

---


<!-- ===== Part decomposition + Vendor-Part + Order Mgmt entities ===== -->
## Part Type Decomposition (Pillar 1)

The legacy `PartType` enum (Part / Assembly / RawMaterial / Consumable / Tooling / Fastener / Electronic / Packaging) overloaded three concepts into one field. It's been decomposed into three orthogonal axes per `phase-4-output/part-type-field-relevance.md`:

1. **`Part.ProcurementSource`** (`ProcurementSource` enum: Make / Buy / Subcontract / Phantom) — how the part is sourced. Subcontract = entire part outsourced (vendor builds it, we never touch it); Make + an `Operation.IsSubcontract = true` op = we make most of it but send out for one step.
2. **`Part.InventoryClass`** (`InventoryClass` enum: Raw / Component / Subassembly / FinishedGood / Consumable / Tool) — which inventory bucket the part lives in.
3. **`Part.ItemKindId`** (FK to `reference_data` group `part.item_kind`, admin-configurable) — descriptive taxonomy: Fastener, Electronic, Packaging, Hardware, Material, etc.

Legacy `PartType` column kept on the row for two release cycles for rollback safety. New code reads the three axes; the workflow adapter (`PartWorkflowAdapter`) accepts EITHER the legacy `partType` OR the new axes in `initialEntityData` and falls back to a derived mapping. Same fallback exists client-side in `parts.component.ts` (`inferAxesFromLegacyPartType`).

**Tier 0 additions on Part**: `TraceabilityType` enum (None / Lot / Serial — replaces `IsSerialTracked` boolean), `AbcClass` (was an unused enum, now a column), `ManufacturerName`, `ManufacturerPartNumber` (engineering OEM identity, distinct from `VendorPart.VendorMpn` for the distributor case).

**11 viable (procurement × inventory_class) combinations** are documented in the audit; per-combination workflow definitions are a Pillar 6 deliverable. Today the existing 2 workflow definitions (assembly-guided + raw-material-express) still drive but with axes populated. Tier 2 fields (Material → MaterialSpecId FK, mass/dimensions/volume measurement profile) are deferred.

## Vendor-Part Intersection (Pillar 3)

`VendorPart` entity captures the (Vendor, Part) relationship with vendor-scoped sourcing metadata: vendor's part number, vendor's manufacturer-part-number when distributing someone else's part, per-vendor lead time / MOQ / pack size, country of origin, HTS code, AVL approval flag, preferred flag, certifications, last-quoted date.

`VendorPartPriceTier` 1:N child captures tiered pricing (`MinQuantity` ≤ requested qty wins; effective-from/to dates).

**API**: `/api/v1/vendor-parts` for CRUD, `/api/v1/parts/{partId}/vendor-parts` for the part-detail Sources tab data, `/api/v1/vendors/{vendorId}/vendor-parts` for the vendor-detail Catalog tab data, plus `/{id}/price-tiers` POST/DELETE for tier upserts.

**Capability**: `CAP-MD-VENDORS`. Roles: `Admin, Manager, Engineer, OfficeManager`.

**Preferred-uniqueness invariant**: at most one VendorPart per Part may have `IsPreferred=true`. Setting it true unsets it on every other VendorPart for the same Part within one SaveChanges (handled in CreateVendorPart + UpdateVendorPart).

`Part.PreferredVendorId` stays — points at the canonical preferred vendor. `Part.MinOrderQty` / `Part.PackSize` / `Part.LeadTimeDays` are kept on Part as a snapshot of the preferred VendorPart's values (backward-compat with existing readers); Phase 2/4 work will migrate readers to the VendorPart row.

---

## Order Management Entities

### New Core Entities (in `forge.core/Entities/`)
```
SalesOrder, SalesOrderLine
Quote, QuoteLine
Shipment, ShipmentLine
CustomerAddress
Invoice, InvoiceLine          ← ⚡ standalone mode
Payment, PaymentApplication   ← ⚡ standalone mode
PriceList, PriceListEntry
RecurringOrder, RecurringOrderLine
```

### New Enums
`SalesOrderStatus`, `QuoteType`, `QuoteStatus`, `ShipmentStatus`, `InvoiceStatus`, `PaymentMethod`, `CreditTerms`, `AddressType`

---


<!-- ===== Pluggable Integrations (backend) ===== -->
## Pluggable Integrations

### Mock Integration Flag
- `MockIntegrations` in appsettings.json (default `false`, `true` in Development)
- `MockIntegrations=${MOCK_INTEGRATIONS:-false}` in docker-compose.yml
- Program.cs conditionally registers mock vs real services based on this flag
- All mock services log operations via `ILogger` for dev visibility

### Accounting (`IAccountingService`)
- Interface: `forge.core/Interfaces/IAccountingService.cs`
- Models: `forge.core/Models/AccountingModels.cs` (AccountingCustomer, AccountingDocument, AccountingLineItem, AccountingPayment, AccountingTimeActivity, AccountingSyncStatus)
- Mock: `forge.integrations/MockAccountingService.cs` — returns canned data matching seeded customers
- QuickBooks Online is default + primary provider — **implemented** (`forge.integrations/QuickBooksAccountingService.cs`): OAuth 2.0, sync queue, customer/item/invoice/payment/time-activity sync, token encryption via Data Protection API
- Additional providers (Xero, FreshBooks, Sage) implement same interface — **not yet implemented** (interface + factory ready)
- App works fully in standalone mode (no provider) — financial features degrade gracefully
- Sync queue, caching, orphan detection are provider-agnostic

### Shipping (`IShippingService`)
- Interface: `forge.core/Interfaces/IShippingService.cs`
- Models: `forge.core/Models/ShippingModels.cs` (ShipmentRequest, ShippingAddress, ShippingPackage, ShippingRate, ShippingLabel, ShipmentTracking, TrackingEvent)
- Mock: `forge.integrations/MockShippingService.cs` — returns 3 mock carrier rates
- Direct carrier integrations: UPS, FedEx, USPS, DHL (not yet implemented — each implements `IShippingService` directly, no middleman)
- Manual mode always available (no API, user enters tracking number)
- **Address validation is NOT part of IShippingService** — see `IAddressValidationService` below

### Address Validation (`IAddressValidationService`)
- Interface: `forge.core/Interfaces/IAddressValidationService.cs`
- Decoupled from shipping — address validation uses USPS Web Tools directly (free)
- Mock: `forge.integrations/MockAddressValidationService.cs` — format-only validation (state codes, ZIP regex, required fields)
- Real: `forge.integrations/UspsAddressValidationService.cs` — USPS Address Information API v3 (XML REST, free with USPS Web Tools User ID)
- Config: `UspsOptions` (`Usps:UserId` in appsettings.json) — register at https://www.usps.com/business/web-tools-apis/
- Program.cs: USPS when User ID configured, mock otherwise (same pattern as other integrations)
- Frontend: `AddressFormComponent` → `AddressService.validate()` → `POST /api/v1/addresses/validate` → `IAddressValidationService.ValidateAsync()`
- USPS returns DPV (Delivery Point Validation) confirmation + standardized address

### AI (`IAiService` — Optional)
- Interface: `forge.core/Interfaces/IAiService.cs`
- Models: `forge.core/Models/AiModels.cs` (AiSearchResult)
- Mock: `forge.integrations/MockAiService.cs` — returns canned text responses
- Self-hosted Ollama + pgvector RAG — **implemented** (`OllamaAiService.cs`): gemma3:4b, `DocumentEmbedding` entity (pgvector vector(384)), RAG pipeline (IndexDocument / RagSearch / BulkIndexDocuments handlers), `DocumentIndexJob` (Hangfire 30 min), `AiController` (generate/summarize/status/search/index)
- Use cases: smart search, job description drafting, QC anomaly detection, document Q&A, header AI search column with RAG results
- Graceful degradation when AI container is down

### Storage (`IStorageService`)
- Interface: `forge.core/Interfaces/IStorageService.cs`
- Real: `forge.integrations/MinioStorageService.cs` (MinIO S3-compatible)
- Mock: `forge.integrations/MockStorageService.cs` — in-memory ConcurrentDictionary
- Config: `MinioOptions` in `forge.core/Models/MinioOptions.cs`

### PDF Form Extraction (pdf.js + PuppeteerSharp)
- **Architecture:** pdf.js (via PuppeteerSharp headless Chromium) extracts text + form fields from PDFs. Smart parser infers ComplianceFormDefinition layout. AI verifies and refines.
- **Full docs:** `docs/pdf-extraction-pipeline.md`
- **3 interfaces:**
  - `IPdfJsExtractorService` — raw pdf.js extraction (text items + annotations)
  - `IFormDefinitionParser` — converts raw data → ComplianceFormDefinition JSON
  - `IFormDefinitionVerifier` — structural checks + AI refinement loop (max 3 iterations)
- **Real:** `PdfJsExtractorService.cs` (PuppeteerSharp singleton browser), `FormDefinitionParser.cs`, `FormDefinitionVerifier.cs`
- **Mock:** `MockPdfJsExtractorService.cs` — returns canned extraction data
- **JS extraction page:** `forge.api/wwwroot/pdf-extract.html` — bundled pdf.js, called via PuppeteerSharp `EvaluateFunctionAsync`
- **Docker:** API container uses Debian base (not Alpine) with Chromium installed. `PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium`
- **Pattern detection:** Step sections, amount lines, filing status, signature blocks, form headers — all inferred from structural cues, no per-form hardcoding

---


<!-- ===== What NOT to Do + Efficiency/Memory ===== -->
## What NOT to Do

- Never use `FormsModule` / `ngModel` in features — always `ReactiveFormsModule`
- Never use raw `<input>`, `<select>`, `<textarea>` — always shared wrappers
- Never build custom dialog shells — always `<app-dialog>`
- Never hardcode colors, spacing, font sizes, border radius in component SCSS
- Never use `*ngIf` / `*ngFor` — use `@if` / `@for`
- Never use `!important` unless overriding third-party (with comment)
- Never nest SCSS more than 3 levels
- Never use "DTO" suffix — use `*ResponseModel` / `*RequestModel`
- Never send date-only strings to the API — always include time + UTC zone
- Never put multiple classes/enums/components in one file
- Never use barrel files (`index.ts`) for re-exports
- Never use inline templates or inline styles
- Never use function calls in template bindings — use computed signals
- Never use constructor injection — use `inject()`
- Never use `console.log` in production code
- Never hardcode z-index values — use `$z-*` variables
- Never use `try/catch` in controllers — middleware handles exceptions
- Never use data annotations on entities — use Fluent API configuration
- Never hard-delete records — always soft delete via `DeletedAt`
- Never use `mat-error` / inline validation — wrap the disabled submit button with `<app-validation-button>` (stereotype). Do not use `ValidationPopoverDirective` on new code.
- Never deep-override Material internals with CSS — build a custom component instead
- Never put HTTP calls in components — always in services
- Never use `*` or `ng-deep` to override child component styles
- Never suppress lint/analysis warnings without a comment explaining why
- Never write data-fetching code without evaluating loading state — use `LoadingService` (global) or `LoadingBlockDirective` (section-level)
- Never duplicate `@keyframes spin` — it's defined globally in `_shared.scss`
- Never build financial features (invoices, payments, AR, P&L, vendor CRUD) without checking the accounting boundary — see below
- Never store significant UI state (tabs, selected entity, filters, pagination) in signals/services alone — the URL must be the source of truth (see "URL as Source of Truth" pattern)
- Never hardcode lists into selects, autocompletes, or multi-selects — options must come from the database via API (roles, statuses, categories, teams, etc.). The only exceptions are truly static UI choices (sort direction, pagination sizes).

---

## Efficiency & Memory Leak Prevention (Non-Negotiable)

**Every code change must be evaluated for memory leaks and efficiency.** These rules prevent the most common resource leaks found in this codebase.

### Angular — Subscription & Resource Lifecycle

1. **Every `.subscribe()` in a service constructor or component constructor MUST have `takeUntilDestroyed(this.destroyRef)` in its pipe chain.** Router events, FormControl.valueChanges, and interval observables are the most common offenders. The only exception is fire-and-forget HTTP calls that complete naturally (single POST/PATCH/DELETE with `catchError`).

2. **SignalR hub services MUST call `.off()` on all registered event names before re-registering or on disconnect.** Otherwise, each `connect()` call accumulates duplicate handlers. Pattern:
   ```typescript
   private registerHandlers(): void {
     this.unregisterHandlers(); // Always clean up first
     this.connection.on('event', (e) => this.callback?.(e));
   }
   private unregisterHandlers(): void {
     this.connection?.off('event');
   }
   ```

3. **Never use `.subscribe()` without error handling on user-facing HTTP calls.** At minimum, add a `catchError` in the pipe or an `error` callback. Silent failures cause state inconsistency.

4. **Global event listeners (`document.addEventListener`, `window.addEventListener`) MUST have corresponding `removeEventListener` in `ngOnDestroy` or via `destroyRef.onDestroy()`.** Track handler references as class fields.

5. **Computed signals must not perform O(n*m) filtering.** Pre-group data with `Map` or `Set` before filtering. Example: instead of `users.map(u => jobs.filter(j => j.assigneeId === u.id))`, pre-build a `Map<userId, Job[]>` and look up by key.

### .NET — Query & Resource Efficiency

1. **Never use `db.Entity.Where()` inside a LINQ `.Select()` projection.** This creates N+1 queries. Pre-load related data with `.Include()`, a JOIN, or a dictionary lookup before the projection.

2. **Never load entire tables into memory** (`await db.Parts.ToListAsync()`). Use pagination (`Skip/Take`), filtering, or chunked processing for large datasets. Hangfire jobs are especially prone to this — process in batches of 500.

3. **Never filter a list inside a loop** (`list.Where(x => x.Id == item.Id)` per iteration). Pre-group with `.GroupBy().ToDictionary()` or `.ToLookup()` before the loop to avoid O(n²).

4. **Hangfire job methods MUST accept `CancellationToken` as a parameter** and pass it to all async calls. Hangfire passes a CT automatically when jobs are cancelled/shut down.

5. **Methods returning `Stream` must document ownership** — prefer returning `byte[]` unless streaming is required for large files. Callers of stream-returning methods must use `using` statements.

6. **Use `AsNoTracking()` on all read-only EF Core queries** (those that don't call `SaveChangesAsync` afterward). Tracking adds memory overhead for change detection.

7. **Add database indexes for columns used in WHERE/JOIN/ORDER BY** — especially foreign keys, `UserId`, and any column used in global query filters.

---

