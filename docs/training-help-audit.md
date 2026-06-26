# Training & "Help for this Page" — Audit (shipped)

_Audit date: 2026-06-25 (prod DB then: 154 modules, 8 paths). Implemented + verified 2026-06-26.
This is the closed-out record of the audit; the work below is **done** (merged to `main`,
deploy-held with the rest of the training overhaul). For the original per-item TODO tables, see
the git history of `docs/training-help-audit-TODO.md`._

## How it works

- The header **"Help for this page"** panel (`training-context-panel`) calls
  `GET /training/modules/by-route?route=<current route>`.
- `GetModulesByRoute.cs` returns published modules whose `AppRoutes` (a JSON string array)
  **bidirectionally prefix-match** the route: a module on `/parts` shows on `/parts` and
  `/parts/123`; a module on `/admin` shows on **every** `/admin/*` page.
- Content is **seeded**, idempotent-by-slug, by the `TrainingContent/*.cs` classes (wired in
  `SeedData.Training.cs`) + the paths in `PathDefinitions.cs`. Per-locale overlays live in
  `Data/Seeds/training-i18n/<locale>/` and are applied by `TrainingTranslationSeeder`. Editing a
  seeder/translation + redeploy repopulates; **no frontend change is needed** — the panel is
  fully data-driven.
- The LMS is **not capability-gated** (CAP-HR-TRAINING was removed 2026-06-25), so every page's
  help panel is reachable on every install.

## Current state (post-audit)

- **48 `*Training.cs` seeders** (47 feature seeders + `PageSpecificTraining`) + `PathDefinitions.cs`.
- **203 base modules + 13 role paths**, each also machine-translated to **Spanish**
  (`Data/Seeds/training-i18n/es/`, applied as an `?lang=` overlay with English fallback).
- All driver.js walkthroughs target live selectors (the 41 detached steps found in a sequential
  audit were fixed).

## What shipped (audit sections A–E)

- **A — mis-routed modules (fixed):** `CustomerReturnsTraining` now tags `/customer-returns`
  (was `/sales-orders`); `ProductionLotsTraining` now tags `/lots` (was `/quality`); the dead
  `/customers/42/orders` placeholder was removed from `CustomersTraining`'s `AppRoutes` (it now
  survives only as a *correct answer* in the customers quiz illustrating the `/customers/:id/:tab`
  URL pattern — intentional).
- **B — coverage gaps (added):** new seeders for the 19 blank pages —
  `AccountingTraining` (the 9-page `/accounting/*` gap, ⚡ accounting-boundary aware),
  `PayablesTraining`, `PurchasingTraining`, `MrpTraining`, `OeeTraining`, `SchedulingTraining`,
  `ApprovalsTraining`, `EmployeesTraining`, `MaintenanceTraining` — all registered in
  `SeedData.Training.cs`.
- **C — page-specific help (added):** `PageSpecificTraining` covers the 13 complex
  inherits-only routes (`/admin/carriers`, `/admin/capabilities`, `/admin/discovery`,
  `/admin/presets`, `/admin/currencies`, `/admin/role-templates`, `/admin/automations`,
  `/customers/segments`, `/customers/portal-access`, `/leads/queue`, `/leads/campaigns`,
  `/leads/intake`, `/sales-orders/recurring`).
- **D — over-tagging (trimmed):** `/training` over-tagging cut from ~22 seeders to **2**;
  `/dashboard` crowding cut to **5** (and `ChatTraining` no longer tags `/dashboard`).
  `/customers/42/orders` and `/inventory/stock` are no longer coverage tags (the only remaining
  `/inventory/stock` reference is the inventory walkthrough's launch `appRoute`, which is correct).
- **E — paths (added):** 5 new role paths — Quality Inspector, Accounting/Bookkeeper,
  Maintenance Technician, Warehouse/Inventory Clerk, Shipping & Receiving — bringing the total to
  **13**, with existing paths updated to include the new Section-B modules.
- **Plus (beyond the original audit):** the **Chat** modules were rewritten off the stale flat
  DM/group-room model onto the current Channels/Teams model — channels grouped under teams,
  threads, @mentions and record links, announcements/broadcast, channel roles, mute, and the
  corrected single-line composer (commit `2b8ee2da`).

## Remaining / deferred

- **`EstimatesTraining` routing (the one open A-item).** It still tags `/customers` and its
  walkthrough tours the customer list. There is **no standalone `/estimates` route** — estimates
  are a `QuoteType` (Estimate | Quote) surfaced on the customer detail **Estimates** tab and under
  `/quotes`. So the audit's "move to `/quotes`" was a *review* call, not a clear win: re-pointing
  it would require rewriting the walkthrough steps (which currently target the customer-list DOM)
  to avoid a detached tour. Left as-is pending a deliberate decision on where estimate help should
  live. Low impact — estimates help is reachable on `/customers`, just not on `/quotes`.

## Implementation recipe (for future seeders/paths)

1. New seeder: copy an existing `TrainingContent/<Feature>Training.cs`; set Title / Slug
   (stable, kebab-case) / Summary / ContentType (Article | Walkthrough | QuickRef | Quiz) /
   `AppRoutes` (JSON array) / EstimatedMinutes / Tags. Register the class in `SeedData.Training.cs`.
2. New path: add to `PathDefinitions.cs` with its module slugs + `AllowedRoles`; set
   `IsAutoAssigned` only for universal onboarding.
3. Translations: add a matching entry (by slug) to each `Data/Seeds/training-i18n/<locale>/*.json`
   so non-English installs stay in parity. Structure (walkthrough selectors/sides, quiz
   ids/options/`isCorrect`, QuickRef item counts) must mirror the English base.
4. Seeders are **idempotent by slug** and update existing rows on re-seed; the deploy-startup seed
   adds/updates without duplicating, and `BackfillEnrollmentsAsync` auto-enrolls eligible users
   into auto-assigned paths. No frontend work — the panel is data-driven.
