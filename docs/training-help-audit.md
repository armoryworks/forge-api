# Training & "Help for this Page" — Audit & TODO

_Audit date: 2026-06-25. Source of truth at audit time: prod DB (154 modules, 8 paths) +
`forge.api/Data/TrainingContent/*.cs` (37 feature seeders + `PathDefinitions.cs`)._

> **STATUS — IMPLEMENTED (2026-06-26).** Sections A–E are all shipped to `main` (merged,
> deploy-held with the rest of the training overhaul). The seeder set is now 47 feature
> classes + `PageSpecificTraining.cs`; the base LMS is **203 modules + 13 paths**, each also
> machine-translated to Spanish (`Data/Seeds/training-i18n/es/`). The 41 detached walkthrough
> steps were fixed, and the Chat modules were rewritten off the stale flat-DM model onto the
> current Channels/Teams model (commit `2b8ee2da`). This doc is retained as the audit record;
> the A–E sections below describe the work that was done, not open work.

## How it works (so the TODO makes sense)

- The header **"Help for this page"** panel (`training-context-panel`) calls
  `GET /training/modules/by-route?route=<current route>`.
- `GetModulesByRoute.cs` returns published modules whose `AppRoutes` (a JSON string array)
  **bidirectionally prefix-match** the route: a module on `/parts` shows on `/parts` and
  `/parts/123`; a module on `/admin` shows on **every** `/admin/*` page.
- Content is **seeded**, idempotent-by-slug, by the 37 `TrainingContent/*.cs` classes (wired in
  `SeedData.Training.cs`) + the 8 paths in `PathDefinitions.cs`. Editing a seeder + redeploy
  repopulates; **no frontend change is needed** — the panel is fully data-driven.
- Note: as of 2026-06-25 the LMS is **no longer capability-gated** (CAP-HR-TRAINING removed),
  so every page's help panel is reachable on every install.

## Coverage snapshot (88 navigable pages)

| Class | Count | Meaning |
|------|------|---------|
| **Dedicated** | 31 | a module's `AppRoutes` matches the page directly |
| **Inherits-only** | 38 | only shows a parent's generic help (mostly `/admin/*`, `/customers/*`, `/leads/*`) |
| **Empty (gap)** | 19 | **no module matches at all — panel is blank** |

---

## A. FIX — mis-routed modules (help is wired to the WRONG page)

These seeders exist and have good content, but tag a sibling route, so the owning page is blank
**and** the help shows up where it doesn't belong. Highest priority — pure wins, no new content.

| Seeder | Current `AppRoutes` | Should be | Effect today |
|--------|--------------------|-----------|--------------|
| `CustomerReturnsTraining.cs` (3 modules) | `["/sales-orders"]` | `["/customer-returns"]` | `/customer-returns` blank; returns help pollutes Sales Orders |
| `ProductionLotsTraining.cs` (3 modules) | `["/quality"]` | `["/lots"]` (or `["/lots","/quality"]`) | `/lots` blank; lots help only on Quality |
| `EstimatesTraining.cs` (review) | `["/customers"]` | `["/quotes"]` likely | Estimates help shows on Customers; `/quotes` page = Quotes-only help. Walkthrough also drives the customer list, not estimates — revisit the steps. |
| `CustomersTraining.cs` | includes `"/customers/42/orders"` | remove it | Hardcoded placeholder route with a literal id `42` — dead/stale. |

## B. ADD — new training for pages with NO help (the 19 gaps)

No seeder references these at all. Create a new `*Training.cs` (copy the shape of e.g.
`ShipmentsTraining.cs`), register it in the `seeders[]` array of `SeedData.Training.cs`.

| Page(s) | New seeder | Notes |
|---------|-----------|-------|
| `/accounting/*` — AP/AR aging, balance-sheet, bank-rec, cash-flow, GRNI, period-close, P&L, trial-balance (**9 pages**) | `AccountingTraining.cs` | Biggest gap. 1 overview tagged `/accounting` (covers all 9 by prefix) + QuickRefs for the heavy ones (period-close, bank-rec, GRNI). ⚡ accounting-boundary aware. |
| `/payables` (AP / vendor bills) | `PayablesTraining.cs` | Distinct from Payments. |
| `/purchasing` (RFQs) | `PurchasingTraining.cs` | Distinct from `PurchaseOrdersTraining` (`/purchase-orders`). |
| `/mrp` | `MrpTraining.cs` _or_ extend `PlanningTraining` with `/mrp` | |
| `/oee` | `OeeTraining.cs` | |
| `/scheduling` | `SchedulingTraining.cs` | |
| `/approvals` | `ApprovalsTraining.cs` | Cross-cutting approval inbox. |
| `/employees` | `EmployeesTraining.cs` | No People/HR training exists today. |
| `/maintenance/predictions` | `MaintenanceTraining.cs` | `KanbanTraining`'s `/maintenance` is the maintenance **kanban track**, not this page. |

## C. ADD (lower priority) — page-specific help for "inherits-only" pages

The 38 inherit a parent's generic help. Most simple admin config pages are fine that way; these
are complex enough to deserve their own module (tag the exact route):

- `/admin/carriers` (carrier API credentials — new this cycle, has setup nuance)
- `/admin/capabilities`, `/admin/discovery`, `/admin/presets` (the capability system)
- `/admin/currencies`, `/admin/role-templates`, `/admin/automations`
- `/customers/segments`, `/customers/portal-access`
- `/leads/queue`, `/leads/campaigns`, `/leads/intake`
- `/sales-orders/recurring`

(The remaining `/admin/*` sub-pages can keep the generic `/admin` help.)

## D. REMOVE / TRIM — unnecessary / noisy

- **`/training` over-tagging (main one):** ~22 feature intro modules tag **both** their feature
  route **and** `/training`. So the Help panel on the Training page renders ~22 cards (every
  feature's intro) — redundant with the Training library's own module grid. Drop `/training`
  from the feature seeders; keep it only on `NavigationTraining`/`OnboardingTraining`/
  `DashboardTraining`.
- **`/dashboard` crowding:** 15 modules tag `/dashboard` (the most of any page) — `ChatTraining`,
  `NotificationsTraining`, `SearchTraining`, `NavigationTraining`, `OnboardingTraining`,
  `DashboardTraining`. Trim to the genuine dashboard-orientation set; drop `ChatTraining`'s
  `/dashboard` tag (chat help on the dashboard is noise).
- **`/customers/42/orders`** placeholder (see A) — remove.
- **`/inventory/stock`** (`InventoryTraining`) — redundant; `/inventory` already prefix-covers it.

## E. PATHS — update + add new

Existing 8 (`PathDefinitions.cs`): New Employee Onboarding _(auto-assigned)_, Office Manager,
Production Engineer, Project Manager, Purchasing, Sales, Shop Floor Worker, System Administrator.

- **ADD new role paths:** Quality Inspector, Accounting / Bookkeeper, Maintenance Tech,
  Warehouse / Inventory Clerk, Shipping & Receiving.
- **UPDATE existing paths** once Section B modules exist: add Accounting modules → Office Manager
  + new Accounting path; RFQ/Payables modules → Purchasing path; MRP/scheduling → Production
  Engineer / Project Manager.
- Re-check module counts after edits (Shop Floor Worker has only 7 — likely thin).

## Implementation recipe

1. New seeder: copy an existing `TrainingContent/<Feature>Training.cs`; set Title / Slug
   (stable, kebab-case) / Summary / ContentType (Article | Walkthrough | QuickRef | Quiz | Video)
   / `AppRoutes` (JSON array) / EstimatedMinutes / Tags. Register the class in `SeedData.Training.cs`.
2. New path: add to `PathDefinitions.cs` with its module slugs + `AllowedRoles`; set
   `IsAutoAssigned` only for universal onboarding.
3. Seeders are **idempotent by slug** — the deploy-startup seed adds new modules without
   duplicating; `BackfillEnrollmentsAsync` auto-enrolls eligible users into auto-assigned paths.
4. No frontend work for help coverage — the panel is data-driven. (Section C/D only touch seeders.)

## Suggested order

A (mis-routes — instant wins) → B (the 9-page accounting gap + payables/purchasing/approvals) →
D (`/training` + `/dashboard` trims) → E (paths) → C (nice-to-have page-specific help).
