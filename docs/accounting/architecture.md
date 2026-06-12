---
title: Accounting Module — Architecture & Integration Spec
type: technical
status: in-progress
id: accounting-module-architecture
updated: 2026-06-08
---

# Accounting Module — Architecture & Integration Spec

**Context:** Standalone manufacturing ERP (Armory Plastics — injection molding).
**Stack:** Angular front end · .NET API · PostgreSQL.
**Costing method:** Standard costing.
**Status:** Design spec. Component IDs follow `AREA-NNN` for cross-reference with the existing test suite (`PHASE-AREA-NNN`). Where a component is already realized on this branch, the mapping is recorded in [§11 Codebase Anchors](#11-codebase-anchors--status).

---

## 1. Purpose & Scope

This document specifies the accounting module: the general ledger (GL) at its core, the internal subledgers that post to it, and the external financial-institution integrations that feed or drain it. It defines the ledger invariants, the posting contract every integration must satisfy, a starter posting map, and a prioritized build sequence.

The guiding decision already made: this is a **purpose-built system of record**, not a QuickBooks wrapper. QuickBooks remains only as a one-way downstream export for the CPA.

---

## 2. Core Principles (Ledger Invariants)

These are non-negotiable. Every component below conforms to them.

1. **Append-only.** Ledger entries are immutable once posted. There is no `UPDATE` or `DELETE` on posted journal entries. This is why the module's surface area is overwhelmingly read; that is correct, not incomplete.
2. **Corrections are new entries.** A mistake is fixed by posting a *reversing* entry plus the correct one — never by editing or deleting. Error handling adds rows.
3. **Balanced entries only.** The GL accepts a journal entry only if total debits equal total credits. Unbalanced entries are rejected at the posting boundary, never persisted.
4. **The GL does not originate transactions.** It *receives* balanced journal entries from subledgers and from a small, controlled set of native operations (manual JE, period close, opening balances). Native manual writes are the gated exception — permissioned, approved, audited.
5. **Source linkage on every entry.** Each posted entry references its originating event/document (`source_type`, `source_id`, `source_event_id`). This makes posting traceable and idempotent.
6. **Idempotent posting.** A retried post of the same source event must not double-book. The posting API dedupes on the source event key.
7. **The single write boundary is "post a balanced JE."** No module writes individual ledger rows directly. If it can, the integrity control is gone.

---

## 3. Architecture Overview

```
                       ┌─────────────────────────────┐
   Subledgers ────────▶│   GL Posting API            │
   (post balanced JEs) │   - validate balanced       │
                       │   - dedupe on source key    │──▶  General Ledger
   Native ops ────────▶│   - append only             │     (append-only,
   (manual JE, close)  └─────────────────────────────┘      immutable)

   External institutions interact through the Integration Layer
   (every adapter sits behind MOCK_INTEGRATIONS):

     READ  : balances, transactions, statements  ─┐
     WRITE : payment origination (NACHA, etc.)    ─┤── adapters ──▶ reconciliation
     RECONCILE : bank stream matched, not posted  ─┘                & posting
```

Two distinct classes of contributor:

- **Internal subledgers** — your own operational modules. They generate journal entries as a byproduct of business events. This is where a *manufacturing* accounting system earns its value.
- **External institutional integrations** — third-party APIs/files. Each offloads something with compliance or operational liability you don't want to own.

The reconciliation stream is deliberately *not* a posting source. Bank data is an independent record matched against expected entries; the **match result** posts, the raw feed does not.

---

## 4. Prioritized Build Roadmap

Ranked so each phase unblocks the next and value lands early. Rationale follows each phase.

| Phase | Theme | Components | Why here |
|-------|-------|------------|----------|
| **0** | Ledger Foundation | GL core, Chart of Accounts, JE engine, Posting API, Period management | Nothing posts without this. Hard prerequisite. |
| **1** | Manufacturing Core | Inventory/WIP valuation + variances, Job/Work-order costing | The differentiator. This is what makes it a manufacturing system rather than a checkbook. |
| **2** | Procure-to-Pay & Cash Out | AP + three-way match (GRNI), Cash/Bank reconciliation, NACHA ACH origination | Closes the spend side and the bank-out path. Depends on inventory receipts (Phase 1). |
| **3** | Revenue & Receivables | AR, Invoicing, Cash receipts, Payment acceptance | Closes the money-in side. Depends on order/shipping events. |
| **4** | Compliance Offload | Sales/use tax engine, Payroll provider integration, QuickBooks export | Each carries filing/liability risk. Prioritized as soon as the flows they touch exist. |
| **5** | Extended Integrations | Fixed assets/depreciation, EDI (850/810/856), Carrier/shipping | Sequencing/optional. EDI is entirely customer-dependent. |

**Top-three to build first within the operational core:** inventory/WIP valuation with variances, job costing, and P2P three-way match. **Top-two external to prioritize:** tax engine and payroll provider — they carry compliance liability. Everything else is sequencing.

---

## 5. Component Specifications

### Phase 0 — Ledger Foundation

#### `GL-001` General Ledger Core
- **Purpose:** Append-only system of record for all financial activity.
- **Key entities:** `journal_entry` (header), `journal_line` (DR/CR detail), `account`, `accounting_period`.
- **Invariants:** §2 in full. Lines reference accounts; header carries source linkage and posting timestamp.
- **Notes:** Store amounts as exact decimal (PostgreSQL `numeric`), never float. Use `DateTimeOffset` end-to-end for posting timestamps (consistent with prior clock-skew fix).

#### `GL-002` Chart of Accounts
- **Purpose:** Account hierarchy and classification (asset/liability/equity/revenue/expense; plus contra and variance accounts).
- **Key detail:** Must include the **variance accounts** standard costing requires (material price, material usage, labor rate, labor efficiency, overhead absorption) and **GRNI** (goods-received-not-invoiced) clearing.

#### `GL-003` Posting API
- **Purpose:** The single write boundary. `post(JournalEntry)` — validates balanced, dedupes on `source_event_key`, appends.
- **Contract:** Rejects unbalanced; idempotent on retry; returns the posted entry ID or the existing one on dedupe.

#### `GL-004` Period Management & Close
- **Purpose:** Open/close accounting periods; block posting into closed periods; roll up trial balance.
- **Native operations:** opening balances, period-end accruals, close. All gated and audited.

---

### Phase 1 — Manufacturing Core

#### `INV-001` Inventory / WIP Valuation
- **Purpose:** Value and track Raw Material → WIP → Finished Goods; post each movement.
- **Posting events:** receipt, consumption (issue to WIP), completion (WIP → FG), shipment (FG → COGS).
- **Standard costing:** movements valued at standard; deltas to actual generate variances → variance accounts. **Material usage** and **purchase price** variances originate here.
- **Notes:** This is the most useful set of numbers the system produces for a molding shop. It only works if movements post cleanly.

#### `JOB-001` Job / Work-Order Costing
- **Purpose:** Roll up material, labor, machine time, and absorbed overhead per work order.
- **Posting events:** material issue to job, labor applied, overhead absorbed (via machine/labor hours), job completion variance.
- **Management view:** cost per part run, scrap impact, margin by job. This is the gap QB did badly that motivated the build.
- **Depends on:** `INV-001` (material issues), `LAB-001` (labor/time).

---

### Phase 2 — Procure-to-Pay & Cash Out

#### `AP-001` Accounts Payable + Three-Way Match
- **Purpose:** PO → Receipt → AP Invoice, with GRNI accrual between receipt and invoice.
- **Posting events:** receipt posts GRNI accrual (DR Inventory / CR GRNI); matched invoice clears GRNI (DR GRNI / CR AP); payment clears AP.
- **Match rule:** quantity and price tolerance between PO, receipt, and invoice; exceptions queued for review.

#### `BANK-001` Cash / Bank Reconciliation
- **Purpose:** Independent bank stream matched against expected entries. **Not a posting source** — the match result posts.
- **Read channels (by availability):**
  - Aggregator API (Plaid/MX/Finicity) — tokenized, **webhook-driven fetch** (pull on notification, not interval polling).
  - Direct bank file feed (BAI2 prior-day, OFX/QFX) — **scheduled daily fetch** aligned to the bank's overnight posting window. Likely path for Frontier CU.
- **Anti-pattern:** naive timer polling. Underlying ACH/deposit data is batch; sub-daily polling buys rate-limit pain, not freshness.
- **Flow:** bank txns land in staging → auto-match clean items → queue exceptions for human review → post match result.

#### `BANK-002` Payment Origination (Cash Out)
- **Purpose:** Move money out for AP and payroll.
- **Mechanism:** generate **NACHA ACH file** → human approval → submit via bank's secured channel (SFTP / treasury portal). Record intent; reconcile settlement back later via `BANK-001`.
- **Controls (mandatory):**
  - **Segregation of duties:** the process that *creates* a payment file is not the one that *releases* it.
  - **No stored bank login credentials.** Tokenized aggregator items or bank-issued API credentials in a secrets manager; read scopes read-only.
- **Open item:** confirm with Frontier CU whether they offer ACH origination, file format, cutoff times, and submission channel. This shapes the whole write side.

---

### Phase 3 — Revenue & Receivables

#### `AR-001` Accounts Receivable & Invoicing
- **Purpose:** Customer invoicing and receivable tracking.
- **Posting events:** invoice (DR AR / CR Revenue; relieve FG → COGS on ship); credit memo (reversal-style entry).
- **Timing:** ship-to-invoice trigger (see `SHIP-001`).

#### `AR-002` Cash Receipts & Payment Acceptance
- **Purpose:** Apply incoming payments to AR.
- **Channels:** B2B is check/ACH-heavy; card/ACH-in via processor (Stripe-style) where used. Receipt → AR application → GL.

---

### Phase 4 — Compliance Offload

#### `TAX-001` Sales / Use Tax Engine
- **Purpose:** Rate, nexus, and exemption handling. **Do not hand-roll.**
- **Integration:** Avalara / TaxJar-style API at invoice time.
- **Manufacturer specifics:** resale certificates, manufacturing exemptions, use tax on consumables. Exemption-heavy by nature.

#### `PAY-001` Payroll Provider Integration
- **Purpose:** Offload **tax filing and deposits** (the liability-heavy part). You may still originate the net-pay ACH yourself via `BANK-002`, but the provider owns filings.
- **Integration:** pull payroll register back → post the payroll JE (labor distribution to jobs/overhead + liabilities).
- **Depends on:** `LAB-001` for hours.

#### `QB-001` QuickBooks Export
- **Purpose:** One-way downstream push of completed financial transactions for the CPA. QB is a passive compliance/reporting recipient, never system of record.
- **Mechanism:** scheduled export in a format the accountant's QB ingests; tolerate malformed/edge-case handling on their side.

---

### Phase 5 — Extended Integrations

#### `FA-001` Fixed Assets & Depreciation
- **Purpose:** Capitalize presses and **mold tooling**; post monthly depreciation as a recurring scheduled JE.
- **Note:** tooling may be customer-owned or per-run-amortized — model ownership explicitly. Low complexity; could be pulled earlier if useful.

#### `LAB-001` Labor / Time Collection
- **Purpose:** Scan-based floor time feeding **two posting paths from one event** — job cost (and overhead absorption via machine hours) *and* payroll.
- **Cross-cutting:** referenced by `JOB-001` and `PAY-001`. Build the capture early even if payroll posting comes later.

#### `EDI-001` Customer EDI (X12 850 / 810 / 856)
- **Purpose:** Inbound PO (850), outbound invoice (810), ASN (856).
- **Conditional:** entirely dependent on whether Armory's customers require it. Larger distributors/OEMs may mandate it. Do not build speculatively.

#### `SHIP-001` Carrier / Shipping
- **Purpose:** Ship confirmation triggers invoicing (ship-to-invoice timing) and freight feeds landed cost.

---

## 6. Integration Contract

Every contributor — subledger or external adapter — meets the same contract at the GL boundary:

- **Emits balanced journal entries**, never individual ledger rows.
- **Carries source linkage:** `source_type`, `source_id`, `source_event_key`.
- **Is idempotent:** re-emitting the same `source_event_key` dedupes, never double-books.
- **External adapters expose a uniform interface** (read / write / reconcile) and sit behind `MOCK_INTEGRATIONS`.

This uniformity is the point: a bank reconciliation result, a job-completion posting, and an AP three-way match are the *same shape* at the ledger boundary.

---

## 7. Starter GL Posting Map

Representative event → debit/credit pairs. Not exhaustive; the integration contract for each subledger.

| Event | Source | Debit | Credit |
|-------|--------|-------|--------|
| Material received (PO) | `AP-001` | Raw Material Inventory | GRNI (clearing) |
| AP invoice matched | `AP-001` | GRNI (clearing) | Accounts Payable |
| Purchase price variance | `AP-001`/`INV-001` | PPV (variance) | GRNI / AP |
| Material issued to job | `INV-001`/`JOB-001` | WIP | Raw Material Inventory |
| Material usage variance | `INV-001` | Material Usage Variance | WIP |
| Labor applied to job | `LAB-001`/`JOB-001` | WIP | Accrued Labor / Payroll Clearing |
| Overhead absorbed | `JOB-001` | WIP | Overhead Applied |
| Job/WIP completed | `JOB-001`/`INV-001` | Finished Goods | WIP |
| Goods shipped | `SHIP-001`/`AR-001` | COGS | Finished Goods |
| Customer invoiced | `AR-001` | Accounts Receivable | Revenue (+ Sales Tax Payable) |
| Sales tax computed | `TAX-001` | (within invoice) | Sales Tax Payable |
| Customer payment received | `AR-002` | Cash (in transit) | Accounts Receivable |
| Vendor paid (ACH) | `BANK-002` | Accounts Payable | Cash (in transit) |
| Bank reconciliation match | `BANK-001` | Cash | Cash (in transit) |
| Payroll posted | `PAY-001` | Labor distribution / Expense | Payroll Liabilities / Cash |
| Depreciation (monthly) | `FA-001` | Depreciation Expense | Accumulated Depreciation |
| Manual adjustment | Native (`GL-004`) | per entry | per entry |

"Cash (in transit)" / clearing accounts deliberately decouple the *intent* to move money from the *confirmed settlement*, so origination (`BANK-002`) and reconciliation (`BANK-001`) post separately.

---

## 8. MOCK_INTEGRATIONS Pattern

Every external touchpoint lives behind a single service interface with a mock implementation for dev/test and a real implementation in prod — same code path either way.

Representative bank interface:

```
fetchTransactions(account, sinceCursor)   -> BankTxn[]      // BAI2/OFX or aggregator
submitPaymentBatch(nachaFile)             -> SubmissionRef  // SFTP / portal
getReconciliationStatement(account, date) -> Statement
```

- **Mock:** returns canned BAI2/NACHA fixtures, including **malformed and failure cases**, so error handling and reconciliation exceptions are exercised without a live account.
- **Real:** SFTP / aggregator / processor implementation.
- Same applies to tax, payroll, payment acceptance, EDI, carrier, and the QB export.

---

## 9. Controls & Audit

- **Append-only + reversing-entry corrections** (§2) are the primary integrity control.
- **Segregation of duties** on all money-out: create ≠ release.
- **Manual JEs** permissioned, approved, audited; cannot be deleted.
- **Period close** blocks back-posting into closed periods.
- **Source linkage** gives a full audit trail from any ledger entry back to its originating business event.
- **No bank credentials in the app;** secrets in a manager, read scopes read-only.

---

## 10. Open Decisions — RESOLVED 2026-06-12 (owner ratification)

1. **Frontier CU / BANK-002 write channel → NACHA file.** Phase A: in-app NACHA generation + manual portal upload (the upload IS the create≠release SoD control). Phase B: SFTP automation + in-app release queue once the CU's origination agreement/spec/cutoffs are confirmed (inquiry still owed to the CU). Phase C: ACH returns (R-code) ingestion. Portal RPA/scraping explicitly rejected (credentials + MFA + ToS + fragility on a money path).
2. **Read channel → manual OFX/CSV statement import** into a staging + auto-match flow (no stored bank credentials, no vendor dependency); an aggregator (Plaid-class) is the sanctioned upgrade if cadence ever needs to be sub-monthly. The CFPB §1033 caveat stands — never architect around mandated bank APIs.
3. **Tooling asset model → explicit per-asset ownership** (Asset.IsCustomerOwned; customer-owned molds off balance sheet, memo-tracked) **+ units-of-production depreciation by shot count** for company-owned molds (CurrentShotCount / ToolLifeExpectancy are already captured operationally), straight-line fallback.
4. **Customer EDI → config-driven minimal core, pared from an OSS translator** (specific fork candidate to be confirmed; LICENSE CHECK REQUIRED before embedding — GPL-class licenses cannot be embedded here). Per-partner implementation guides stay data-driven (`EdiMapping`); no partner-specific hardcoding until a customer mandates.
5. **Payroll split → full-service provider owns filing AND funds net pay**; Forge pulls the register and posts the JE (the PayRun foundation is the consumer). No in-house tax calculation, ever.
6. **Standard cost roll cadence → annual roll + gated ad-hoc re-rolls** (controller-approved, with inventory revaluation), supported by an automated **variance watchdog** — threshold/trend monitors on the variance accounts notifying the controller with a friendly digest and a "consider a cost roll" suggestion.

**Additional ratifications (same date):** split `CAP-P2P-BILL` / `CAP-P2P-PAY` from `CAP-P2P-PO`; build the **open-item AR/AP sub-ledger now** (no conversion dependency); QB-001 = **CSV/Excel export always available + QBO API journal push config-gated** (reusing the existing QuickBooks OAuth plumbing) — not mutually exclusive; cutover strategy (parallel-run vs hard cut) is an **adoption-time client decision** documented in the runbook, not a code path; accounting migrations ride the next build, with the Cash-in-Transit account backfill made idempotent in the seeder so pre-seeded installs need no manual SQL.

---

## 11. Codebase Anchors & Status

> *Added on commit (2026-06-08) to tie the spec to what already exists on this branch (`feat/accounting-gl-phase1`). The spec §1–§10 above is authoritative for intent; this section records the current realization so reviewers can see design-vs-built at a glance. Keep it updated as components land.*

### Platform alignment (how this fits the existing app)

- **Capability gate.** The entire built-in GL is gated behind **`CAP-ACCT-FULLGL`** — registered in `forge.api/Capabilities/CapabilityCatalog.cs` and, as of this writing, an **off-by-default "dark" capability** (the nav group is hidden unless it's enabled). It also sits opposite the existing accounting-boundary mutex **`CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN`**: this module *is* the built-in system-of-record path. `QB-001` (§5, Phase 4) is the one-way downstream export — consistent with "QB is never the system of record."
- **Operational vs ledger separation (important).** The shipped **manual inventory override** (`CAP-INV-ADJUST`, `POST /inventory/set-on-hand`) is deliberately **operational-only and posts no JE** (see the inventory-override design). `INV-001` here is its *ledger-posting counterpart*, which activates only under `CAP-ACCT-FULLGL`. Per the boundary's dependency rule, **the GL subscribes to inventory/job events; operational modules never call the GL** — preserving invariant #4 and #7. Subledger → GL is one-directional through the Posting API.
- **Clock.** "Use `DateTimeOffset` end-to-end" (§GL-001) is satisfied by the injectable `IClock` / `SystemClock` abstraction already used app-wide (the prior clock-skew fix). Posting timestamps must come from `IClock`, never `DateTime.UtcNow`.
- **MOCK_INTEGRATIONS** (§8) is the established app pattern — every external service is `interface` + real impl + mock impl, switched by the `MockIntegrations` flag. Bank/tax/payroll/QB adapters follow the same shape as the existing accounting/shipping/address/AI/storage integrations.

### Component → code map (Phase 0 / Phase 1 realized on this branch)

| Component | Realized by (this branch) |
|-----------|---------------------------|
| `GL-001` GL core | `forge.core/Entities/Accounting/`: `JournalEntry`, `JournalLine`, `GlAccount`, `Book`, `CostCenter`, `LedgerBalance`, `AcctNumberSequence`. Amounts as `numeric` (decimal). |
| `GL-001`/§2 invariants #1–#2 (append-only, no edit/delete) | `forge.data/Interceptors/LedgerImmutabilityInterceptor.cs` (registered in `AppDbContext.OnConfiguring`) **and** DB triggers (migration `AddLedgerImmutabilityTriggers`) — enforced in software *and* at the database. |
| `GL-002` Chart of Accounts | `GlAccount` (+ `AccountType`, `ControlAccountType` enums) + `AccountDeterminationRule` (event→account mapping). Seeded CoA in `forge.api/Data/SeedData.Accounting.cs` includes Inventory RM/WIP/Subassemblies/FG, AP, AR, COGS, **GRNI**, and the standard-cost variance set: PPV (51000), Material Usage (51100), Production (51200), Labor Rate (51300), Labor Efficiency (51310), Overhead Spending (51320). |
| `GL-004` Period management | `FiscalYear`, `FiscalPeriod`, `JournalEntryStatus` enum, `GlCapability` enum. |
| `GL-003`/`GL-004` native manual JE + maker-checker (§2 #4, §9) | `CreateManualJournalEntry` posts through the single boundary (`IPostingEngine`). **Async maker-checker:** a manual JE over the book's `MakerCheckerThreshold` with no up-front distinct approver routes to `PendingApproval` (numbered but **not** folded into `LedgerBalance` — invisible to the trial balance) instead of posting; a *distinct* approver finalizes via `ApprovePendingAsync` (→ `Posted` + applied) or rejects to `Draft`. Endpoints `GET /accounting/journal-entries/pending`, `POST .../{id}/{approve,reject}`. |
| `INV-001`/`JOB-001` conversion-cost absorption | `MaterialIssuePostingService` (material → WIP), `ProductionReceiptPostingService` (WIP → FG at standard), and `ProductionVariancePostingService` (job-cost close): absorbs labor + overhead **+ subcontract** into WIP (Dr `INVENTORY_WIP` / Cr `LABOR_APPLIED`·`OVERHEAD_APPLIED`·`SUBCONTRACT_APPLIED` 51210/51220/51230), then sweeps the WIP-by-job residual to `PRODUCTION_VARIANCE` (decomposed into the 6 standard-cost variance slots when a standard resolver is wired). |
| `INV-001`/`JOB-001`/`AP-001`/`BANK-*` etc. | UI realized on `feat/accounting-gl-phase1` (forge-ui: trial-balance, P&L, balance-sheet, cash-flow, AR/AP aging, GRNI, bank-rec, period-close) — refactored to the shared design system (`app-data-table`, `.action-btn` taxonomy, SCSS tokens) and **auto-refreshing live** via an `AccountingHub` SignalR push (`GlChangeBroadcastInterceptor` broadcasts on any GL write). Posting wiring per the roadmap (§4) is largely in place; multi-currency + realized FX (`AR-002`/`AP-001` settlement) in progress. **`AR-002`/`AP-001` open-item sub-ledger realized** (2026-06-12): per-document `ArOpenItem`/`ApOpenItem` rows maintained at posting time inside the control-moving transactions (origination creates; payment applications increment at the booking-rate relief; voids restore; a voided bill's item → `Voided`, excluded from both reconciliation sides like its reversed GL) — per-document aging (DueDate, DocumentDate fallback; partial pay shrinks the document's bucket — replaced the balance-forward re-bucketing), control-reconciled (AR/AP control balance vs Σ open functional; manual JEs hitting control directly surface as the reconciliation difference by design), booking-rate functional amounts; `EnsureOpenItemsBackfilledAsync` backfill ensure on boot (empty-table-guarded, rebuilds from posted originations matched by idempotency keys, applied only from posted payments). Migration `AddOpenItemSubLedger`. |
| `QB-001` QuickBooks export | **Realized** (2026-06-12): three CSV exports for the CPA (`GET /accounting/exports/{trial-balance,gl-detail,journal-summary}.csv`, RFC-4180, CAP-ACCT-FULLGL) + one-way QBO journal-summary push behind `CAP-ACCT-QBO-EXPORT` (default OFF, NOT in the BUILTIN⊥EXTERNAL mutex — it exports FROM the built-in GL): `QboAccountMap` (GlAccount→QBO account), unmapped-accounts 409, one balanced QBO JE per period, `QboExportLog` with force-repush guard; reuses the existing QuickBooks OAuth/token substrate. UI: `accounting/exports` screen (date range, CSV downloads, mapping editor + push behind `*appCap`). QB never system of record. |
| `FA-001` Fixed assets / tooling | **Realized** (2026-06-12): `DepreciationMethod.UnitsOfProduction` — molds depreciate by shot count via `FixedAsset.LinkedAssetId`→operational `Asset.CurrentShotCount` with a `LastDepreciatedUnits` high-water mark, capped at NBV → FullyDepreciated; customer-owned tooling REJECTED at capitalization (off balance sheet per §10.3); straight-line default unchanged. |
| §10.6 variance watchdog | **Realized** (2026-06-12): daily `variance-watchdog` job (CAP-ACCT-FULLGL-gated) checks each variance account vs admin-tunable thresholds (`accounting.variance-watchdog.percent-of-cogs` default 5%, `absolute-floor` default 500); one friendly Controller notification per (account, period) with a standard-cost-roll nudge on material variances. |

### Outstanding (not yet anchored)

- `GL-003` Posting API as the *single, enforced* write boundary (validate-balanced + dedupe-on-`source_event_key`) — confirm all subledger writes route through it and nothing writes `journal_line` directly (invariant #7).
- The §7 posting map encoded as `AccountDeterminationRule` rows (vs hard-coded), so the CoA mapping is data-driven.
- Per-source idempotency key persisted + unique-indexed (invariant #6).
- ~~UI i18n: the Phase-1 forge-ui components currently render hardcoded strings — externalize to `en.json`/`es.json` before the suite leaves "dark" status.~~ **DONE** (2026-06-08): the 10 accounting screens are internationalized — top-level `accounting.*` section (108 keys) in `en.json` + `es.json` at 1:1 parity; server-supplied dynamic labels and table aria-labels intentionally left.

### `BANK-002` (partial) — Payment transmission pipeline (added 2026-06-11)

The retry/backoff/triage half of `BANK-002` is realized on this branch; NACHA file generation and the real
bank submission channel remain open per §10.1.

| Piece | Realized by |
|-------|-------------|
| Transmission record | `forge.core/Entities/PaymentTransmission.cs` — generic polymorphic source (`SourceType`/`SourceId`, mirrors ActivityLog/StatusEntry), status `Queued → Retrying → Succeeded \| Failed \| Cancelled`, attempt/backoff/error/bank-ref columns. Migration `AddPaymentTransmission`. |
| Bank channel seam | `IBankPaymentService` (`forge.core/Interfaces`) + `MockBankPaymentService` (`forge.integrations`) — ALWAYS mock until the Frontier CU channel is decided (§10.1); a reference number containing `FAIL` forces the failure path deterministically. |
| Retry engine | `forge.api/Jobs/PaymentTransmissionJob.cs` — 1 initial attempt + 4 retries, exponential backoff ×4 (1/4/16/64 min) via Hangfire `Schedule`; all times from `IClock`. |
| Triage | Final failure → status `Failed`, `transmission-failed` activity row, **critical AppNotification to the payment creator**; `GET /api/v1/payment-transmissions?status=&sourceType=` lists the queue and `POST /api/v1/payment-transmissions/{id}/retry` re-queues with a fresh 5-attempt cycle (AttemptCount reset to 0 — deliberate: a manual reprocess earns a full cycle; history stays on the activity log). |
| Origination hook | `CreateVendorPayment` queues a transmission for electronic methods (`PaymentMethods.IsElectronic`: `BankTransfer`/`Wire`) after the payment (and posting) commits. |
| Cash in transit (§7 realized) | Electronic vendor payments post their origination cash credit to **`CASH_IN_TRANSIT` (10150)** instead of `CASH` (`VendorPaymentCashPostingService`) — recording the *intent* to move money. On transmission success, `PaymentTransmissionJob` posts the **settlement** entry `Dr CASH_IN_TRANSIT / Cr CASH` for the origination's exact in-transit functional amount (idempotency key `AP:VendorPayment:{id}:SETTLEMENT`; skipped silently when no origination JE exists or it predates CIT; a settlement-posting failure never fails the transmission — the lingering CIT balance is the visible reconciling item). Non-electronic methods still credit `CASH` directly. |
| Void path | `POST /vendor-payments/{id}/void` (`VoidVendorPayment`): reverses the origination JE (incl. realized-FX plug), drops bill applications (bills reopen), cancels any non-terminal transmission, soft-deletes the payment. **Hard-blocked once the latest transmission Succeeded** — corrections then require a new transaction; `RetryPaymentTransmission` likewise rejects voided payments. AR `VoidPayment` reverses its cash-receipt JE the same way. |
| Sweep (self-healing) | `PaymentTransmissionSweepJob` (recurring `payment-transmission-sweep`, every 5 min): re-enqueues up to 50 stuck rows per run — Queued never attempted >10 min after creation, or Retrying whose `NextAttemptAt` passed >10 min ago (lost Hangfire schedule). Double-enqueues are harmless (worker status checks + engine idempotency). |
| UI surfacing | Latest-transmission fields on `VendorPaymentListItemModel`/`VendorPaymentDetailModel`, `HasFailedTransmission` on `VendorBillListItemModel`/`VendorBillDetailModel`; `PaymentTransmission` added to `GlChangeBroadcastInterceptor`'s watch list so status changes push `accountingChanged`. |

**Semantics note: "Succeeded" = the bank ACCEPTED the submission, NOT that the money settled.** Actual
settlement is confirmed by `BANK-001` reconciliation matching the bank stream against the in-transit
balance — which is exactly why origination and settlement post separately through `CASH_IN_TRANSIT` (§7).

Controls note: the §"Controls (mandatory)" segregation (create ≠ release) and the human-approval step before
submission are NOT yet implemented — they belong to the real-channel work, not the mock pipeline.
