# Forge Training Coverage Audit

**Date:** 2026-06-26

## What this is

A synthesis of 52 per-feature training audits for the Forge ERP. Each audit compared the **user-facing functionality** that ships in a feature against the **training content** that exists for it (the four standard module types: Article, Walkthrough, QuickRef, Quiz), and rated every core capability as `taught: yes / partial / no`.

## Methodology

- **Per-feature audit.** Each feature was walked end-to-end against its live routes/components and its seeded training modules.
- **Core vs edge split.** Capabilities were classified as **core** (something a normal user of that feature must be able to do — the common-sense bar) or **edge** (power-user, admin-config, validation-limit, or internal-mechanic detail). Edge items become side-doc topics, not training gaps.
- **Verdict scale.**
  - `well-covered` — all/nearly-all core capabilities taught; remaining gaps are edge/polish.
  - `minor-gaps` — mostly covered, but one or more real core capabilities are untaught or mis-taught.
  - `major-gaps` — an entire workflow, route, or primary surface is missing or actively wrong.
- **Accuracy note.** Several audits flag content that is not merely missing but *wrong* (teaches a UI/workflow that does not exist, or describes retired behavior). Those are called out explicitly because they mislead, not just under-serve.

---

## Summary table

| Feature | Verdict | Core gaps (taught=no) | Partial | Side-doc topics |
|---|---|---|---|---|
| Kanban Board | well-covered | 0 | 1 | 5 |
| Backlog | minor-gaps | 3 | 1 | 5 |
| Planning Cycles | minor-gaps | 1 | 1 | 4 |
| MRP | minor-gaps | 1 | 2 | 4 |
| Scheduling | minor-gaps | 1 | 2 | 4 |
| OEE | well-covered | 0 | 0 | 3 |
| Production Lots / Traceability | minor-gaps | 0 | 6 | 5 |
| Quality (insp/NCR/CAPA/SPC/ECO/gages) | minor-gaps | 0 | 3 | 5 |
| Maintenance (predictive / PM) | well-covered | 1 | 0 | 5 |
| Assets / Tooling | minor-gaps | 4 | 3 | 6 |
| Shop Floor Kiosk | major-gaps | 4 | 2 | 7 |
| Parts (+ BOM / vendor-parts) | minor-gaps | 1 | 3 | 6 |
| Vendors (+ vendor-parts / AVL) | major-gaps | 5 | 1 | 7 |
| Inventory | minor-gaps | 2 | 1 | 5 |
| Customers | major-gaps | 5 | 2 | 7 |
| Leads | major-gaps | 9 | 1 | 6 |
| Quotes & Estimates | minor-gaps | 4 | 0 | 4 |
| Sales Orders (+ recurring) | major-gaps | 6 | 2 | 5 |
| Shipments | major-gaps | 3 | 3 | 4 |
| Customer Returns (RMA) | major-gaps | 2 | 6 | 5 |
| Invoices | well-covered | 1 | 1 | 5 |
| Payments | well-covered | 0 | 0 | 2 |
| Purchasing / RFQs | well-covered | 0 | 0 | 2 |
| Purchase Orders (+ receiving) | major-gaps | 6 | 1 | 7 |
| Payables / Vendor Bills | minor-gaps | 3 | 1 | 4 |
| Expenses | major-gaps | 8 | 1 | 5 |
| Approvals | well-covered | 0 | 1 | 3 |
| Accounting suite | minor-gaps | 2 | 0 | 5 |
| Employees / HR | well-covered | 0 | 0 | 2 |
| Time Tracking | well-covered | 0 | 0 | 4 |
| Payroll | minor-gaps | 1 | 3 | 4 |
| Compliance Forms | major-gaps | 4 | 3 | 7 |
| Chat | minor-gaps | 1 | 2 | 5 |
| Notifications | minor-gaps | 0 | 3 | 4 |
| Calendar & Events | minor-gaps | 1 | 1 | 5 |
| Global Search | minor-gaps | 0 | 2 | 3 |
| AI Assistant | major-gaps | 4 | 3 | 5 |
| Reports | well-covered | 0 | 1 | 5 |
| Dashboard (widgets/gridstack) | major-gaps | 7 | 3 | 4 |
| Navigation & Onboarding | major-gaps | 5 | 4 | 8 |
| EDI | minor-gaps | 4 | 1 | 5 |
| MFA & Account Security | minor-gaps | 2 | 1 | 5 |
| Capability system | minor-gaps | 0 | 2 | 5 |
| Carrier integrations admin | minor-gaps | 1 | 3 | 6 |
| Reference Data & Terminology admin | minor-gaps | 2 | 3 | 4 |
| Users / Roles / Teams admin | minor-gaps | 3 | 3 | 4 |
| Announcements admin | major-gaps | 6 | 3 | 5 |
| Automations / Assignment Rules / Auto-PO admin | major-gaps | 6 | 0 | 4 |
| Integrations / Connections admin | minor-gaps | 3 | 5 | 5 |
| Sales Tax & Currencies admin | minor-gaps | 1 | 4 | 3 |
| Lead scoring admin (ICP/completeness/sources) | major-gaps | 9 | 0 | 5 |
| Company Settings / Audit / Time Corrections / Track Types / API keys admin | minor-gaps | 3 | 1 | 5 |

**Verdict tally:** 11 well-covered · 27 minor-gaps · 14 major-gaps (52 total).

---

## Core gaps to teach (Phase B fill list)

Every core capability rated `taught: no` or `partial`, grouped by feature, ordered **major-gaps features first**, then minor-gaps. Each line names the recommended new (▶ NEW) or updated (◆ UPDATE) module.

### MAJOR-GAPS FEATURES

#### Shop Floor Kiosk
- **(no)** Inventory Scan station — 8-action scan hub (Move/Count/Receive/Issue/Ship/Inspect/Job/Return); overlay also fires on the main display. ▶ NEW: Inventory Scan Station (Article + Walkthrough)
- **(no)** Kiosk pairing / terminal setup — admin login → name terminal → pick/create team → device token; gates both main display and clock kiosk. ▶ NEW: Setting Up a Shop Floor Kiosk (Article)
- **(no)** Reverse/undo a recent scan action (PIN-gated, 6-hour lookback). ◆ UPDATE: Field Reference + Overview (Undo/Reverse)
- **(no)** Daily Scan Log (filter by date/action, summary counts). ◆ UPDATE: Field Reference + Overview (Daily Log)
- **(partial)** Dedicated Clock kiosk dual-scan flow + manual-login fallback. ▶ NEW: Clock Kiosk Walkthrough
- **(partial)** Storage-location scan type (opens location-contents overlay). ◆ UPDATE: Field Reference (add storage-location scan type)

#### Vendors (+ AVL / price tiers)
- **(no)** Vendor Catalog / AVL tab — add/edit/remove vendor-parts, bulk CSV import. ▶ NEW: Vendor Catalog (Article + Walkthrough)
- **(no)** Approved (AVL) + Preferred Source flags. ▶ NEW: Vendor Catalog & AVL (Article)
- **(no)** Vendor-part price tiers (quantity breaks, currency, effective dating, history). ▶ NEW: Managing Vendor Pricing — Price Tiers (Walkthrough)
- **(no)** New Vendor fork dialog (Quick add vs Guided). ▶ NEW: Creating a Vendor: Quick vs Guided (Walkthrough); ◆ UPDATE: Guided Tour (fix stale Add-Vendor step)
- **(no)** Guided vendor setup wizard (/vendors/new, 5 steps). ▶ NEW (same Quick-vs-Guided module)
- **(partial)** Quick-add flat dialog framed as THE create flow; off-tier variance field unmentioned. ◆ UPDATE: Field Reference (add off-tier variance)

#### Customers
- **(no)** 3-way create fork (Quick add / Convert from Lead / Guided). ▶ NEW: Creating Customers (Walkthrough + Article); ◆ UPDATE: Guided Tour
- **(no)** Guided customer setup wizard (/customers/new). ▶ NEW (same module)
- **(no)** Customer-specific Pricing tab (price lists, entries, CSV import). ▶ NEW: Customer-Specific Pricing (Article + Walkthrough)
- **(no)** Customer portal access provisioning (/customers/portal-access). ▶ NEW: Customer Portal Access (Article)
- **(no)** Flat cross-customer Contacts hub (/customers/contacts). ◆ UPDATE: Overview/Field Reference
- **(partial)** Lifecycle-driven tab set (taught as fixed 10 tabs — now wrong). ◆ UPDATE: Overview + Quiz cu8
- **(partial)** Inline header Edit mode (taught as an edit dialog). ◆ UPDATE: Overview

#### Leads
- **(no)** New Lead fork dialog (engagement shapes + shape-specific forms). ▶ NEW shapes content; ◆ UPDATE: Guided Tour (replace flat-create steps)
- **(no)** Bulk CSV intake (/leads/intake). ▶ NEW: Leads Intake & Bulk Import (Walkthrough)
- **(no)** Pull-based call queue (/leads/queue, keyboard dispositions, click-to-dial). ▶ NEW: Leads Call Queue (Walkthrough)
- **(no)** Outreach Campaigns (/leads/campaigns). ▶ NEW: Outreach Campaigns (Article + Walkthrough)
- **(no)** Accounts (B2B groupings) + bulk-assign. ▶ NEW: Accounts & Bulk-Assign (Article + Walkthrough)
- **(no)** Sample Shipment tracking (/leads/samples). ▶ NEW: Sample Shipment Tracking (Walkthrough)
- **(no)** Suppression / DNC list (/leads/suppression). ▶ NEW: Suppression & Do-Not-Contact (Article)
- **(no)** Lead classification chips (capability fit / NDA / ITAR). ▶ NEW: Lead Qualification & Classification (Article)
- **(no)** Engagement signals (playbook hint, recent-engagement, stale, recent comms). ▶ NEW (same Qualification module)
- **(partial)** Convert to Customer — taught as two buttons; now one-click. ◆ UPDATE: Overview + Quiz ld4

#### Sales Orders (+ recurring)
- **(no)** 8-tab detail workspace (Overview/Lines/Schedule/Shipments/Returns/Documents/Invoices/Activity). ▶ NEW: SO Detail Workspace (Walkthrough)
- **(no)** In-place Draft line editing (Add/Edit/Delete on Lines tab). ▶ NEW: Editing a Draft Sales Order (Article/Walkthrough)
- **(no)** Edit SO header on Draft (Customer PO / Credit Terms / Requested Delivery). ▶ NEW (same Editing module)
- **(no)** Price-list auto-fill of line unit price on part select. ▶ NEW (same Editing module)
- **(no)** Recurring order templates (/sales-orders/recurring, nightly auto-generation, delete-only). ▶ NEW: Recurring Orders (Article + Walkthrough + QuickRef)
- **(no)** Documents tab (upload/download/delete). ◆ UPDATE: Overview/Field Reference
- **(partial)** Fulfillment tracking summary + per-line job drill-down + no-job warnings. ▶ NEW: Tracking Fulfillment from a Sales Order (Article)
- **(partial)** Invoices tab rollup + uninvoiced-shipment warning. ◆ UPDATE (same Fulfillment article)

#### Shipments
- **(no)** Create-from-order-LINES flow (no free part picker; qty capped at remaining). ▶ NEW: Creating from Sales Order Lines (Walkthrough) — **fixes active accuracy bug** (Q1/Q5 teach a part picker that doesn't exist)
- **(no)** Edit shipment dialog (ship-to address selector/creator, dimensions, tracking/cost/weight). ▶ NEW: Editing, Ship-To & Rates/Labels (Walkthrough)
- **(no)** Download/Regenerate wrapped Ship Document (the taught "Packing Slip PDF" isn't reachable here). ◆ UPDATE: Overview (replace packing-slip section)
- **(partial)** Get-Rates requires a ship-to address first. ◆ UPDATE (same Editing module)
- **(partial)** Label-creates-tracking for API carriers; Mark-Shipped gating. ◆ UPDATE: Overview/Walkthrough
- **(partial)** Mark Shipped suppressed until label/tracking (API carriers). ◆ UPDATE (same)

#### Customer Returns (RMA)
- **(no)** Open a return's detail panel (row click; chips, detail grid, deep-link). ▶ NEW: Customer Returns — Using the UI (Walkthrough)
- **(no)** Activity/audit timeline on the detail panel. ◆ UPDATE: Overview
- **(partial — all API-framed)** List/filter, create dialog, resolve-with-notes, close confirmation, edit, rework-job creation — entire feature is taught as raw API calls ("backend-only, no UI"). ▶ NEW: Using the UI + Resolving and Closing (Walkthroughs); ◆ UPDATE: Overview + Walkthrough + Field Reference + Quiz (de-API-ify)

#### Purchase Orders (+ receiving)
- **(no)** Three-tab structure (Orders / Suggestions / Settings). ◆ UPDATE: Walkthrough (add tab-bar steps)
- **(no)** Freight capture at receipt (actual freight, allocation method, variance). ▶ NEW: PO Shipping, Currency & Freight (Article)
- **(no)** Shipping & Currency on a PO (Incoterm, freight, quote currency, FX). ▶ NEW (same Freight article)
- **(no)** Auto-PO Suggestions (review/convert/dismiss/bulk/Run Analysis). ▶ NEW: Auto-PO Suggestions (Walkthrough)
- **(no)** Auto-PO Settings (enable, Suggest/Draft/Automatic mode, buffer days). ▶ NEW: Auto-PO Suggestions & Settings (Article)
- **(no)** Short-close a partially-received PO (required reason). ▶ NEW: Closing a PO Short (Walkthrough/QuickRef)
- **(no)** Inactive part/vendor lifecycle warnings when building a PO. ◆ UPDATE: Overview
- **(partial)** Purchase Unit (UoM) selection + LIST badge + fractional qty. ◆ UPDATE: Overview/Field Reference. **Also fix**: Overview's "receiving updates inventory when a storage location is specified" line is now inaccurate.

#### Expenses
- **(no)** Attach a receipt (required-receipt policy blocks save). ▶ NEW: Expense Receipts & Vendor-Settled (Article)
- **(no)** Vendor-settled expense → routes to AP / becomes a vendor bill. ▶ NEW (same Receipts article) — **fixes Quiz ex12 which says there's no Vendor column**
- **(no)** Approval Queue (/expenses/approval) + review dialog. ▶ NEW: Expense Approval Queue & Revision (Walkthrough)
- **(no)** Request Revision (NeedsRevision, ≥10-char note). ▶ NEW (same Approval module) — **fixes Quiz ex8**
- **(no)** Resubmit a returned expense (reviewer feedback loop). ▶ NEW: Editing/Resubmitting/Deleting (Walkthrough)
- **(no)** Edit and delete your own expenses. ▶ NEW (same Editing module)
- **(no)** Recurring expenses (/expenses/upcoming — create, pause/activate, delete). ▶ NEW: Recurring & Upcoming Expenses (Article + Walkthrough)
- **(no)** Upcoming-expense 90-day forecast. ▶ NEW (same Recurring module)
- **(partial)** Status model missing the 5th shipped status NeedsRevision. ◆ UPDATE: Overview/Field Reference/Quiz ex14

#### Compliance Forms
- **(no)** Admin: create/edit/delete form templates + flags. ▶ NEW: Compliance Forms for Administrators (Article)
- **(no)** Admin: State Withholding configuration dialog. ▶ NEW: State Withholding Configuration (Article)
- **(no)** Admin/Manager: verify identity docs + send reminders. ▶ NEW: Managing Employee Compliance (Article/QuickRef)
- **(no)** Admin: per-employee compliance review panel (statuses, I-9 sub-states, download signed PDF). ▶ NEW (same Managing module)
- **(partial)** Admin: extract electronic form definition from a PDF + "pending setup" state. ◆ UPDATE (Admin article)
- **(partial)** Admin/Manager: complete I-9 Section 2 (3-business-day deadline). ▶ NEW: Completing I-9 Section 2 (Walkthrough)
- **(partial)** Sign ceremony (DocuSeal) described but not demonstrated. ◆ UPDATE: Walkthrough

#### AI Assistant
- **(no)** Global-header AI Help panel (persistent, streaming, related-training links). ▶ NEW: AI Help & Smart Search (Article) — **fixes the "in-memory only" persistence claim**
- **(no)** Header AI search-suggest (navigate to records). ▶ NEW (same Help & Search article)
- **(no)** Admin: write an assistant's system prompt. ▶ NEW: Configuring AI Assistants (Walkthrough)
- **(no)** Admin: scope an assistant to allowed entity types (RAG grounding). ▶ NEW (same Configuring module)
- **(partial)** AI drafting/summarizing affordance (generate/summarize). ▶ NEW: AI Drafting & Summarizing (Article/QuickRef)
- **(partial)** Admin: create/edit/delete an assistant. ▶ NEW (same Configuring module)
- **(partial)** Admin: starter questions / active / sort. ◆ UPDATE (same Configuring module)

#### Dashboard (widgets/gridstack)
- **(no)** Add a widget (Add Widget menu in edit mode). ▶ NEW: Customizing Your Dashboard (Walkthrough)
- **(no)** Remove a widget (X in edit mode). ▶ NEW (same Customize module)
- **(no)** Reset layout to defaults. ▶ NEW (same Customize module)
- **(no)** Focus Mode (toggle, ?focus URL, persisted). ▶ NEW: Dashboard Display Modes (Article)
- **(no)** Ambient Mode (manual + auto-idle, exit). ▶ NEW (same Display Modes article)
- **(no)** Export dashboard to CSV. ◆ UPDATE: Overview/Field Reference
- **(no)** Action Items widget (complete/dismiss follow-ups). ▶ NEW: Dashboard Widgets Catalog
- **(no)** End of Day widget (Top-3 note) — **seeder instead documents a non-existent Active Timers widget**. ▶ NEW: Widgets Catalog; ◆ UPDATE: Overview/Field Reference/Quiz (remove fictional widgets)
- **(partial)** Customize/edit-mode required before drag/resize (taught as always-on). ▶ NEW: Customize Walkthrough
- **(partial)** Several real widgets (Margin/Inventory Snapshot/Team Load/Deadlines/Activity) untaught; Open Orders fields wrong; Getting Started banner behavior wrong (db7). ▶ NEW: Widgets Catalog; ◆ UPDATE: Overview/Quiz

#### Navigation & Onboarding
- **(no)** New-hire onboarding wizard (/onboarding 7-step paperwork: Personal/Address/W-4/State/I-9/Direct Deposit/Acknowledgments). ▶ NEW: New-Hire Onboarding Wizard (Article + Walkthrough + Quiz)
- **(no)** Review & e-sign forms (PDF preview + DocuSeal loop). ▶ NEW: Review & E-Sign Your Forms (Walkthrough)
- **(no)** AI search results column in header. ◆ UPDATE: Navigation article/Field Reference
- **(no)** Breadcrumb navigation. ◆ UPDATE: Navigation article/Field Reference/Walkthrough
- **(partial)** Sidebar drill-in/drill-out grouped tree (taught as a flat icon list). ◆ UPDATE: Navigation article + Walkthrough + Quiz
- **(partial)** Chat / AI Assistant / Training-context panel header buttons. ◆ UPDATE: Navigation article/Field Reference
- **(partial)** User menu: language switcher + About dialog. ◆ UPDATE: Navigation article/Field Reference
- **(partial)** Dashboard landing: customize/Focus/Ambient/export + Getting Started banner. ▶ NEW: Your Dashboard (Article/Walkthrough) — overlaps Dashboard feature above

#### Announcements admin
- **(no)** Find the Admin > Announcements panel + its two tabs. ▶ NEW: Announcements Admin (Article + Walkthrough)
- **(no)** Review the sent-announcements list (columns, ack count). ▶ NEW (same)
- **(no)** Acknowledgment roster dialog (who acknowledged). ▶ NEW (same)
- **(no)** Create a reusable announcement template. ▶ NEW (same)
- **(no)** Delete a template. ▶ NEW (same)
- **(partial)** Send a new announcement (no authoring walkthrough). ▶ NEW (same)
- **(partial)** Scope: Individual Team / Team Leads Only + Target Teams multi-select. ▶ NEW (same)
- **(partial)** Prefill from a template. ▶ NEW (same)
- *(also ◆ UPDATE ChatTraining to cross-reference the admin panel.)*

#### Automations / Assignment Rules / Auto-PO admin
- **(no)** Assignment Rules CRUD (/admin/assignment-rules). ▶ NEW: Lead Assignment Rules (Article)
- **(no)** Rule Kind (Round Robin / Territory / Industry / Account-Based). ▶ NEW (same)
- **(no)** Rule Priority ordering + Is Active. ▶ NEW (same)
- **(no)** Kind-specific spec fields. ▶ NEW (same)
- **(no)** Auto-PO enable + mode (Suggest/Draft/Automatic). ▶ NEW: Auto-PO (Article)
- **(no)** Auto-PO buffer days + notify-chat + nightly demand-analysis job. ▶ NEW (same); plus ▶ NEW: Reviewing & Converting Auto-PO Suggestions (Walkthrough)

#### Lead scoring admin (ICP / completeness / sources)
- **(no)** Lead Sources catalog (/admin/lead-sources) — Name + immutable Code. ▶ NEW: Lead Sources & Quality Scores (Article)
- **(no)** Per-source Quality Score (auto-computed nightly). ▶ NEW (same)
- **(no)** Deactivate vs delete (delete blocked when leads linked). ▶ NEW (same)
- **(no)** ICP Rubrics CRUD + Active/Default single-default invariant. ▶ NEW: ICP Rubrics & Weighted Dimensions (Article)
- **(no)** Weighted scoring Dimensions editor. ▶ NEW (same)
- **(no)** Match Spec JSON authoring. ▶ NEW: Authoring ICP Match Specs (Walkthrough)
- **(no)** End-to-end purpose (rubric → Lead.IcpScore → work-queue ordering). ▶ NEW (ICP article)
- **(no)** Entity Completeness requirement rows (/admin/entity-completeness). ▶ NEW: Entity Completeness Requirements Admin (Article + Walkthrough)
- **(no)** Predicate JSON authoring + i18n key wiring. ▶ NEW (same)
- *(plus ◆ UPDATE AdminTraining/LeadsTraining/PathDefinitions to surface these tabs.)*

### MINOR-GAPS FEATURES

#### Backlog
- **(no)** Admin: Show Archived / Show Active toggle + archived view. ◆ UPDATE: Overview (add Archived section) + Walkthrough + Field Reference
- **(no)** Admin: row-level Unarchive (with confirm). ◆ UPDATE (same)
- **(no)** Admin: bulk-select + bulk-unarchive archived jobs. ◆ UPDATE (same) + Quiz
- **(partial)** Edit-an-existing-job entry point + immutable track type. ◆ UPDATE: Overview

#### Planning Cycles
- **(no)** Role permissions (workers read-only; Admin/Manager mutate). ◆ UPDATE: Field Reference (Who-can-do-what note)
- **(partial)** Activate action — UI gates on a non-existent "Draft" status, so button never renders. ◆ UPDATE: Overview/Field Reference/Quiz (reconcile; file defect)

#### MRP
- **(no)** Run Detail drill-down (parts-touched, time-phased buckets, pegging). ◆ UPDATE: Overview + Walkthrough + Field Reference + Quiz
- **(partial)** Edit a master schedule + MPS vs Actual variance table. ◆ UPDATE: Walkthrough
- **(partial)** Per-tab status/unresolved filter controls. ◆ UPDATE: Field Reference

#### Scheduling
- **(no)** Forward/Backward + priority rule taught as user choices — UI hardcodes Forward/DueDate (no controls). ◆ UPDATE: Overview + Field Reference + Quiz q4 + Walkthrough (**accuracy fix**)
- **(partial)** "Gantt timeline" is actually a plain data table. ◆ UPDATE: Overview (soften wording)
- **(partial)** Work-center edit/delete-confirm flow. ◆ UPDATE: Walkthrough

#### Production Lots / Traceability
- **(partial ×6)** List screen/columns; on-screen search; New Lot dialog (creation is manual, not auto); inline create-part; fractional Quantity (taught as integer — **wrong**); on-screen trace timeline (taught as API arrays); custom lot-number framing (UI always auto-generates). ▶ NEW: Create a Lot and Read Its Traceability (Walkthrough) + Reading the Lot Trace Timeline (Article); ◆ UPDATE: Overview + Workflow + Field Reference + Quiz

#### Quality
- **(partial)** Record per-check results / finalize — UI has no results-entry surface. ◆ UPDATE: Overview/Walkthrough (reframe to create-only) — ▶ NEW deferred until UI ships
- **(partial)** QC template builder — no builder UI exists. ◆ UPDATE: Overview/Quiz — ▶ NEW deferred until UI ships
- **(partial)** CAPA over-claims fields (root-cause method/tasks/effectiveness) the create dialog lacks. ◆ UPDATE: Field Reference + Quiz

#### Maintenance (predictive)
- **(no)** Schedule Maintenance is an Admin/Manager action (non-managers can't use it). ◆ UPDATE: Overview + Field Reference

#### Assets / Tooling
- **(no)** Acquisition & accounting fieldset (cost, depreciation, work center, GL account). ◆ UPDATE: Overview + Walkthrough + Field Reference + Quiz as5 (**fixes "tooling fields are the only extras"**)
- **(no)** Log/end equipment downtime (CAP-MAINT-BREAKDOWN). ▶ NEW: Tracking Asset Downtime (Walkthrough)
- **(no)** Maintenance schedules + log maintenance + create maintenance job. ▶ NEW: Asset Maintenance Schedules (Walkthrough/Article)
- **(no)** *(grouped)* — covered by the two NEW modules above plus barcode/scan. ▶ NEW: Printing & Scanning Asset Labels (Walkthrough)
- **(partial)** Edit an asset (lightly taught). ◆ UPDATE: Walkthrough
- **(partial)** Barcode/QR label widget in detail panel. ▶ NEW (Labels walkthrough above)
- **(partial)** Update machine operating hours. ◆ UPDATE: Field Reference

#### Parts (+ BOM / vendor-parts)
- **(no)** Promote a Draft part to Active + resolve readiness gates. ◆ UPDATE: Overview + Walkthrough + Field Reference + Quiz
- **(partial)** BOM revision-history panel. ◆ UPDATE: Overview
- **(partial)** Alternates tab CRUD (add/approve/bidirectional/conversion). ◆ UPDATE: Field Reference
- **(partial)** Serials tab (register/history/genealogy). ◆ UPDATE: Walkthrough + Field Reference

#### Inventory
- **(no)** Inventory Home (Kiosk/Tasks/Dashboard) — the DEFAULT landing. ▶ NEW: Inventory Home (Article) + Inventory Home Guided Tour (Walkthrough)
- **(no)** Friendly stock verbs (Receive/Use/Count/Find). ▶ NEW (same Home modules)
- **(partial)** Units of Measure tab CRUD + conversions. ▶ NEW: Units of Measure & Conversions (Walkthrough/Article)

#### Quotes & Estimates
- **(no)** Edit Draft quote line items from the detail panel. ◆ UPDATE: Quotes Overview + Field Reference + Walkthrough
- **(no)** Per-line margin % + cost breakdown on quote detail. ◆ UPDATE: Quotes Overview + Field Reference
- **(no)** Add line items to a saved Draft estimate (taught as "no line items" — **wrong**). ◆ UPDATE: Estimates Overview + Field Reference
- **(no)** *(estimate line itemization)* — same; ◆ UPDATE: Estimates Quiz est2/est6

#### Payables / Vendor Bills
- **(no)** Vendor bank accounts (Accounts tab) — create/dual-control approve/prenote/verify/edit/disable. ▶ NEW: Vendor Bank Accounts & Dual Control (Article + Walkthrough)
- **(no)** NACHA payment batches (Batches tab) — assemble/generate/download/release SoD. ▶ NEW: NACHA Payment Batches (Article + Walkthrough)
- **(no)** Import bank ACH return / NOC file. ▶ NEW (same NACHA module)
- **(partial)** Batches/Accounts tabs only named in passing (gated by CAP-BANK-NACHA). ◆ UPDATE: Overview + Walkthrough + Field Reference

#### Accounting suite
- **(no)** Import & match bank statements (OFX/QFX/CSV auto-match). ▶ NEW: Importing & Matching Bank Statements (Walkthrough); ◆ UPDATE: Overview/Field Reference/Quiz/Walkthrough
- **(no)** Export accounting data (CSV + QBO mapping/push). ▶ NEW: Exporting for Your Accountant / QuickBooks (Article + Walkthrough); ◆ UPDATE (same set)

#### Payroll
- **(no)** Capability gating (CAP-HR-PAYROLL) — Payroll can be hidden per-install. ◆ UPDATE: Overview
- **(partial ×3)** Admin upload location (it's the Admin compliance panel, not /account) + upload-PDF-first mechanic + admin view-per-employee surface. ◆ UPDATE: Overview + Walkthrough + Field Reference + Quiz pr7

#### Chat
- **(no)** Pop out chat into a separate window. ◆ UPDATE: Overview + Walkthrough + Field Reference + Quiz
- **(partial)** @mention/record-link composer — taught but **not wired in the live composer**. ◆ UPDATE: Overview/Walkthrough/Field Reference/Quiz q7 (remove/qualify)
- **(partial)** Announcement *authoring* taught as in-chat; actually lives in Admin > Announcements. ◆ UPDATE: Overview/Walkthrough/Field Reference/Quiz q9

#### Notifications
- **(partial)** Click a notification to navigate to its source entity. ◆ UPDATE: Overview + Field Reference (teach as an action)
- **(partial)** Standalone Notifications page: search/filter/sort. ◆ UPDATE: Walkthrough
- **(partial)** Email preferences — **wrong location** (tour points at User Menu; prefs live at /notifications → Preferences). ◆ UPDATE: Walkthrough

#### Calendar & Events
- **(no)** Click a job chip to open it on the Kanban board. ◆ UPDATE: Calendar Overview + Field Reference + Quiz
- **(partial)** RSVP / attendee status — **no respond UI exists**; "Employee Detail Events tab" doesn't exist. ◆ UPDATE: Events Overview + Field Reference + Quiz (soften/scope as backend/future)

#### Global Search
- **(partial)** Clicking a result opens the entity *detail dialog* (?detail=type:id), not a list page. ◆ UPDATE: Overview + Field Reference + Quiz Q8 (**accuracy fix**)
- **(partial)** Search is desktop-only (hidden on tablet/mobile). ◆ UPDATE: Overview

#### EDI
- **(no)** SFTP transport credential fields + password-kept-on-edit. ◆ UPDATE: Walkthrough + Field Reference
- **(no)** Per-partner Part-Number Map dialog (the real translation feature; seeder teaches a non-existent JSON editor). ▶ NEW: EDI Part-Number Translation (Walkthrough); ◆ UPDATE: Overview + Field Reference + Quiz Q6
- **(no)** Part-Number Map CSV import. ▶ NEW: Part-Number Map CSV Import (Article/QuickRef)
- **(no)** Inbound-850 translation mental model. ◆ UPDATE: Overview
- **(partial)** Edit/delete trading partner flow. ◆ UPDATE: Walkthrough

#### MFA & Account Security
- **(no)** Change your account password (first card on /account/security). ▶ NEW: Account Security Basics (Article) + Manage Your Account Login (Walkthrough)
- **(no)** Set your own kiosk PIN (second card). ▶ NEW (same modules)
- **(partial)** Default device among multiple devices (narrative gap). ◆ UPDATE: MFA Article + Walkthrough

#### Capability system
- **(partial)** Per-capability detail page (relationships, roles, audit log). ▶ NEW: Capability detail page (Article); ◆ UPDATE: capabilities-overview
- **(partial)** Custom configuration screen (hand-toggle, violation gating, reset). ▶ NEW: Custom configuration (Article/Walkthrough); ◆ UPDATE: presets-overview

#### Carrier integrations admin
- **(no)** Walkthrough + QuickRef + Quiz all absent (Article-only feature). ▶ NEW: Carriers Walkthrough + Field Reference + Quiz
- **(partial)** Manual vs Api carrier consequences (Service ID, credentials/test affordances). ◆ UPDATE: Overview
- **(partial)** Delivery Update Mode (Manual/Poll/Webhook) meaning. ◆ UPDATE: Overview
- **(partial)** Edit/delete carrier lifecycle (no edit/deactivate row action). ◆ UPDATE: Overview/side-doc

#### Reference Data & Terminology admin
- **(no)** Reference Data tab is **read-only** — article teaches a non-existent Add/Edit/reorder workflow. ◆ UPDATE: Article rewrite + Admin Overview + Quiz ad12 (**accuracy defect**)
- **(no)** Read the entry-row columns (Order/Code/Label/Effective dates/Status). ◆ UPDATE: Article
- **(partial ×3)** Browse/expand framing; Terminology 3-column layout; default-vs-state-scoped resolution. ◆ UPDATE: Article

#### Users / Roles / Teams admin
- **(no)** Role Templates (rollups) CRUD + system-default read-only. ▶ NEW: Role Templates (Article) + Quiz
- **(no)** Assign a role template on the user form. ▶ NEW (same)
- **(no)** Role-template deletion impact. ▶ NEW (same)
- **(partial)** Create/edit/delete a Team workflow. ▶ NEW: Managing Teams & Members (Walkthrough)
- **(partial)** Assign team members dialog. ▶ NEW (same)
- **(partial)** Share setup code / regenerate token / pending-setup states. ◆ UPDATE: Field Reference

#### Integrations / Connections admin
- **(no)** Connections registry (/admin/connections) — read-only credential audit + Manage deep-links. ▶ NEW: Connections Registry (Article); ◆ UPDATE: Admin Overview + Quiz
- **(no)** Sync Status card (queue depth, failed, last sync). ◆ UPDATE: Integration Configuration article
- **(no)** Sandbox setup guide + developer-portal link. ◆ UPDATE (Field Reference / new QuickRef)
- **(partial ×5)** Standalone/provider switching; Test action + inline result; Configure→Reconfigure→Activate; Disconnect; carrier cards vs /admin/carriers. ◆ UPDATE: Integration Configuration article; ▶ NEW: Integrations Guided Tour (Walkthrough) + Field Reference

#### Sales Tax & Currencies admin
- **(no)** Sales Tax edit/delete a rate. ▶ NEW: Sales Tax Rates (Article + QuickRef)
- **(partial ×4)** Sales Tax list/columns; add-rate workflow; default-vs-state scope meaning; per-customer resolution logic. ▶ NEW (same Sales Tax modules); ◆ UPDATE: Admin Field Reference (add Exempt + GL Posting Account fields)

#### Company Settings / Audit / Time Corrections / Track Types / API keys admin
- **(no)** Time Corrections workflow (filter, edit date/start/end, required Reason, correction history). ▶ NEW: Time Corrections (Article + Walkthrough)
- **(no)** System API keys (issue/copy-once/revoke, user-bound). ▶ NEW: API Keys for Integrations & BI (Article + Quiz)
- **(no)** BI/reporting API keys. ▶ NEW (same API Keys module)
- **(partial)** Pay-period locking workflow (concept-only). ◆ UPDATE: System Settings article (add Pay Period Locking section)

#### Invoices (well-covered, with one real gap)
- **(partial)** PDF export — Article claims it but **no UI button exists** (API-only). ◆ UPDATE: Overview (correct/scope the claim)
- **(no)** Email an invoice — backend-only, no UI. ◆ UPDATE: Overview (flag as future)

---

## Side documentation topics (Phase C backlog)

Consolidated edge-case / ancillary topics by feature. These are reference/power-user/admin-config notes, not training gaps.

- **Kanban Board:** job-cost deep-dive (est/actual/variance, material issues/returns); SO-line link at job creation; BOM-at-release snapshot + stale indicator; CAP-MFG-WO-RELEASE gating of New Job; cover-photo upload + assignee incomplete-profile warning.
- **Backlog:** assignee incomplete-profile warning; view-mode persistence internals; card-grid vs table priority palette inconsistency; 200-job fetch cap; empty/load-error states.
- **Planning Cycles:** CAP-PLAN-MRP gating + empty state; role permissions matrix; server validation limits; dashboard current-cycle selection.
- **MRP:** forecasts→MPS apply + bucket overrides; bulk-release/delete planned orders; Admin/Manager + CAP-PLAN-MRP gating; Net Change vs Full mechanics.
- **Scheduling:** where shifts/calendars are maintained; Asset/Location set elsewhere; not-yet-exposed features (simulate, drag-reschedule, work-center load); CAP-PLAN-CAPACITY + role gating.
- **OEE:** empty/all-clear states; full raw calculation field reference; CAP-RPT-OPERATIONAL gating.
- **Production Lots:** API-only update/delete + soft-delete; CAP-INV-LOTS + roles; deep-link + draft recovery; barcode routes to Quality lot search; bin-location linkage matching.
- **Quality:** SPC control-limit recalculation; CAPA priority scale + source taxonomy; NCR disposition code reference (Reject/Concession); gage calibration-history auto-scheduling; Cpk/Cp thresholds.
- **Maintenance:** manual TriggerRun; ML model registry/performance; work-center risk scores; data dictionary (RUL, model id/version); CAP-MAINT-PREDICTIVE gating.
- **Assets:** depreciation options + cost bounds; GL auto-population via accounting sync; predictive maintenance dashboard; subcontract send-out/receive-back; capability/role gating; asset photo + deep-link.
- **Shop Floor Kiosk:** scanner device pairing; multi-worker sessions; training mode; WebHID RFID relay; per-action scan-flow internals; token revocation/re-pairing; IsShopFloor stage visibility config.
- **Parts:** vendor Sources view modes + tier history (SCD-2); landed cost + manual override; material/UoM canonical units; Purchase History + 3D viewer tabs; serial genealogy; per-combo workflow step differences.
- **Vendors:** off-tier variance % override; vendor performance comparison report; entity-completeness chips; CSV import format/errors; vendor-is-manufacturer toggle; preferred-uniqueness + sole-source rendering; draft auto-save.
- **Inventory:** CAP-INV-MULTILOC single vs multi-location; lot/serial fields + expiration highlighting; bin statuses + QC Hold/Release; ATP; movements-tab filtering.
- **Customers:** accounting-boundary behavior; capability gating of tabs + credit card; Overview compliance/reference flags; recent-communications auto-logging; Segments/Import placeholders; completeness chip; price-list effective dates.
- **Leads:** intake CSV column aliasing + dedup statuses; queue keyboard cheat sheet; suppression cooldown round-trip; sample stale auto-flagging; account-deletion block; engagement-shape JSONB extras.
- **Quotes & Estimates:** detailed cost-estimating tool (mock-backed, unwired); price-list resolution on part select; draft auto-save; validation constraints + chip color mapping.
- **Sales Orders:** Schedule tab at-risk milestones; Returns tab links; fractional qty / 4-decimal pricing; draft auto-save/resume; why detail-line table is read-only post-Draft.
- **Shipments:** packages CRUD; carrier pickup scheduling; packing slip / BOL PDFs (generated elsewhere); ship-to address validation + disabled-customer-addresses fallback.
- **Customer Returns:** CAP-O2C-RMA + roles; RMA numbering + validation limits; rework-job auto-creation internals; In Inspection vs UnderInspection label reconciliation; API/response-model reference.
- **Invoices:** multi-currency selector/FX; invoice queue settings; email/PDF once surfaced; draft auto-save; role + CAP-O2C-INVOICE.
- **Payments:** multi-currency settlement FX rate; future-date restriction on Payment Date.
- **Purchasing/RFQs:** how RFQs reach Cancelled/Expired + edit reachability; Admin/Manager/OfficeManager + CAP-P2P-RFQ gating.
- **Purchase Orders:** off-tier price prompt; below-vendor-minimum warning; FX lock-at-Submit; AI price-variance review; per-line price edit + override-reason audit; PO barcode + calendar feed; draft auto-save.
- **Payables:** multi-currency bills + settlement FX; transmission diagnostics fields; expense-to-bill promotion + void restriction; accounting-provider boundary.
- **Expenses:** expense policy settings + dynamic validators; vendor-bill promotion + one-live-bill invariant; classification taxonomy + recurrence math; direct-URL-only sub-routes; CAP-ACCT-EXPENSES + roles.
- **Approvals:** how an entity enters the inbox (submit-for-approval + threshold matching); delegation/history/active-toggle/delete model-vs-UI gaps; Admin-vs-Manager write split.
- **Accounting suite:** bank-statement import edge cases; export date presets + 409 handling; aging six-bucket support; shared data-table power features; multi-book roadmap.
- **Employees / HR:** roster department filter (wired, not surfaced); shared DataTable power features.
- **Time Tracking:** Admin Time Corrections panel; pay periods/locking/overtime config; shop-floor/mobile clock-in; time→job-costing & payroll (labor/burden, QBO sync).
- **Payroll:** admin upload mechanics; manual vs automatic sync; pay-stub field/validation reference; CAP-HR-PAYROLL gating.
- **Compliance Forms:** AcroForm field-map JSON + blank PDF setup; auto-sync from source URL; form-definition version pinning; I-9 reverification; sensitive-form masking; DocuSeal recovery; system vs custom templates.
- **Chat:** channel-type taxonomy; channel-settings permissions matrix; attachment whitelist + 10MB; announcement authoring deep-dive (admin); CAP-EXT-CHAT gating.
- **Notifications:** full entity-type→route map; mobile notifications page; CAP-CROSS-NOTIFICATIONS gating; dismissed-notification retention/audit.
- **Calendar & Events:** PO-toggle persistence + month-range fetch; data-source rules + 3-chip overflow; event time-input/validation + Hangfire reminder; where events appear read-only; Working Calendars as a distinct admin feature.
- **Global Search:** result distribution/limits + ILIKE matching + dual debounce; RAG routing/scoring + Documentation filtering; desktop-only visibility.
- **AI Assistant:** temperature/maxContextChunks tuning; built-in vs custom + deletion rules; capability gating / container-down degradation; RAG document indexing; Help-panel cross-tab sync + 50-message cap.
- **Reports:** per-report cheat sheet; saved-report sharing/ownership; filter operator reference (Between/In/IsNull); shared data-table features; server-side saved-report export endpoint.
- **Dashboard:** responsive grid breakpoints; layout/preferences persistence model + 5-min refresh; ambient idle-timeout config; Action Items trigger-type catalog.
- **Navigation & Onboarding:** W-4 dependents calc + exempt; I-9 List A vs B+C uploads; no-income-tax states; direct-deposit routing/account; securely-stored fields + draft resume; acknowledgments (workers' comp vs handbook); per-page help tours vs Training panel; dashboard install variants.
- **EDI:** qualifier ID code reference; transport deep reference (AS2/VAN/API); UOM/pack-qty translation not built; control numbers; partner→Customer/Vendor linking (verify).
- **MFA & Account Security:** alternative MFA methods (SMS/Email/WebAuthn); device lockout/recovery; recovery-code/device audit fields; kiosk PIN vs RFID/barcode interplay; password complexity policy.
- **Capability system:** capability audit log + apply Reason field; Discovery branch model + jump-to-recommendation; concurrency/version-mismatch; accounting mutex + CAP-ACCT-FULLGL placeholder; compare matrix power options.
- **Carrier integrations:** Integration Service ID + SCAC; requires-scan-to-ship enforcement; account-number requirements; sandbox→production promotion; CAP-O2C-SHIP gating; carrier active/inactive lifecycle.
- **Reference Data & Terminology:** effective-date windowing + metadata/group_id model; kiosk/anonymous reference reads; terminology key-prefix conventions; where values actually get created (forge-db seed).
- **Users / Roles / Teams:** RFID WebHID pairing + relay setup script; employee barcode generation; kiosk terminals management + team-delete dependency; Work Location visibility + state-withholding linkage.
- **Announcements admin:** Retract/Update API vs UI affordances; system-generated announcements; department-scope in model not surfaced; field-length limits + chip semantics; real-time push into the admin list.
- **Automations / Assignment Rules / Auto-PO:** raw-JSON spec fallback + serialization; capability + per-action role gating; Auto-PO system-setting keys + SO/PO status sets; assignment-rule validation + Territory upper-casing.
- **Integrations / Connections:** mode-change-requires-restart; CAP-IDEN-AUTH-API-KEYS gating + disabled banner; registry status vocabulary + scoping; carrier cards vs /admin/carriers; MOCK_INTEGRATIONS + sandbox guides.
- **Sales Tax & Currencies:** Sales Tax validation + role gating; currency catalog validation + CAP-MD-CURRENCIES; exchange-rate convert endpoint + source provenance + self-pair guard + history filtering.
- **Lead scoring admin:** full predicate-type reference; ICP MatchSpec JSON cookbook; nightly scoring jobs explained; role/capability gating matrix; seed vs admin-authored completeness rows + i18n keys.
- **Company Settings / Audit / API keys:** audit-log retention/immutability + CSV-export reconciliation; system-event presets vs free-text; API-key role-template inheritance + no-self-issue rule; settings secret masking; track-type accountingDocumentType/irreversibility relationship.

---

## Bottom line

Of 52 audited features, **11 (~21%) are well-covered**, **27 (~52%) have minor gaps**, and **14 (~27%) have major gaps** — so roughly **four out of five features carry at least one real core gap**, and over a quarter are missing an entire workflow, route, or primary surface (or actively teach something that does not exist). The synthesized backlog is approximately **70 new modules to author** (concentrated in the major-gaps features: Leads, Lead-scoring admin, Sales Orders, Expenses, Customers, Vendors, Compliance Forms, Dashboard, Navigation/Onboarding, Announcements admin, Auto-PO admin), roughly **75 existing modules to extend** (many of which are accuracy corrections, not just additions), and about **240 side-doc topics** for the Phase C reference backlog. The highest-leverage work is the accuracy fixes — content that teaches non-existent UI (Reference Data Add-Item, Shipments part picker, Dashboard Active Timers, Lots integer quantity, Scheduling Forward/Backward choice, Customer Returns "no UI", Leads two-button convert) — because that content actively misleads users today, not merely under-serves them.
