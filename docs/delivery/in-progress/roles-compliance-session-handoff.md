---
title: Roles + Compliance Session Handoff
type: delivery
status: in-progress
id: roles-compliance-session-handoff
updated: 2026-07-03
---

# Roles + Compliance Session Handoff

Cross-repo pickup doc for resuming on another machine. The umbrella `forge/docs`
tree (where the running `blocking-questions.md` inventory lives) is **not a git
repo**, so this tracked mirror in `forge-api/docs` is the durable handoff. Repos:
`forge-api`, `forge-ui`, `forge-db` (all `github.com/armoryworks`).

## Merged this session (all on `main`, all gate-green)

| Repo | HEAD | What |
|------|------|------|
| forge-api | `24ec65ca` | event workflow read-model + status/ack ActivityLog |
| forge-api | `b9d34bdd` | retire user-side RoleTemplate coupling + ComplianceOfficer role |
| forge-api | `042677f4` | multi-role Create/UpdateAdminUser |
| forge-api | `40f7595e` | CAP-EXT-WATCHTOWER gate on WatchtowerController |
| forge-db  | `32b59e8`  | drop `asp_net_users.role_template_id` (col/FK/index) |
| forge-ui  | `5cb2038c` | dedicated `/compliance` module (scoped calendar) |
| forge-ui  | `c7692743` | multi-select roles admin + role-suggestions info icon |

### The three owner-decision items — DONE
1. **Role model** — users get **multiple roles directly** (`asp_net_user_roles`,
   reconciled in Create/UpdateAdminUser). One-time raw-SQL migration
   (`SeedData.RoleMigration.cs`) expands any assigned role template → direct roles
   then clears the FK (column-existence-guarded). **User-side `RoleTemplate` retired**
   (auth-path expansion, assign/unassign endpoints, `role_template_id` column all
   removed). `role_templates` table **kept** as a named bundle for SystemApiKey
   scoping (Option B — not a dead table). New **`ComplianceOfficer`** role seeded +
   granted read visibility to the 8 regulatory calendar Super-Groups
   (`SeedData.Calendar.cs:SeedComplianceOfficerVisibilityAsync`). forge-ui: multi-select
   + info-icon persona suggestions; template picker removed; role-templates panel
   reframed as API-key bundles.
2. **Watchtower capability** — `WatchtowerController` gated behind `CAP-EXT-WATCHTOWER`
   (EXT, default-OFF, network-dependent). Catalog total: 157.
3. **`/compliance` module** — role-gated forge-ui feature reusing `CalendarComponent`
   via a `scope` input (`module:compliance`); optional `titleKey`/`subtitleKey` keep it
   to one page-header. Surfaces regulatory buckets by default; namespaces saved views.

### Verification
- forge-api: `Release -warnaserror` + 795 tests (Admin/Auth/Accounting/SystemApiKey/
  Calendar/Capabilities) green for the role work; +55 Events/Calendar for the event
  status commit.
- forge-ui: `lint` (0 errors), `lint:i18n` (en/es parity), 1316 tests, `ng build` green.
- CLR machine-load crash (0x80131506) recurs on `dotnet test`; workaround = build once
  `-c Release -warnaserror`, then `dotnet test --no-build -c Release`, retry on crash.

## In flight — pick up here

### 1. Compliance-calendar full status dialog (backend done, frontend next)
**Backend shipped (`24ec65ca`):** `EventResponseModel` now returns `WaivedReason`,
`CompletedByUserId`, `CompletedAt`, `EvidenceUrl`, `EvidenceDocumentSetId`; status +
acknowledge handlers now write ActivityLog. The write path
(`POST events/{id}/status` → `UpdateEventStatusCommand`) already persists
Status/Owner/WaivedReason/EvidenceUrl/EvidenceDocumentSetId.

**Frontend TODO (forge-ui):**
- Extend `features/events/models/event.model.ts` (`AppEvent`) — and, if opened from the
  calendar, `features/calendar/models/calendar-event.model.ts` — with the workflow
  fields (now returned by the API).
- Build `EventStatusDialogComponent`: `<app-dialog>` + `<app-select>` status
  (Open/InProgress/Done/Waived, enum `Forge.Core.Enums.EventStatus`) + `<app-select>`
  owner (from `adminService.getUsers()`; `Last, First` format) + conditional
  `<app-textarea>` waive-reason (when Waived) + evidence `<app-input>` URL + optional
  `<app-file-upload-zone entityType="events">`. Wire with
  `EventsService.updateEventStatus(id, {...})` (already exists) +
  `FormValidationService`/`<app-validation-button>`.
- Swap the one-click **Mark Done** in
  `features/admin/components/events-panel/events-panel.component.ts` for "Set status…"
  opening the dialog (that panel already builds `userOptions` from `getUsers()`).
- Optional: make calendar event chips clickable → open the same dialog (needs the
  `CalendarEvent` model fields + a click handler; chips are read-only today).
- i18n `en.json` + `es.json` (1:1).
- **Do NOT reuse** `SetStatusDialogComponent` (that's the generic polymorphic StatusEntry
  system — different endpoint/shape). Use it only as a structural template.
- Evidence-as-DocumentSet: `EvidenceDocumentSetId` has **no producer endpoint** for
  events today (`IDocumentStore` is Shipments-only; `FilesController` makes
  `FileAttachment`, not DocumentSet). Simplest path = `EvidenceUrl` + FileAttachment via
  `FilesController` (leave `EvidenceDocumentSetId` null). A `POST events/{id}/evidence`
  DocumentSet endpoint is net-new if truly needed.

### 2. Regulated-parts (C) remaining wiring — not started
- GS1 license expiry → renewal-PO **Hangfire job** (reuse purchasing) + a company
  **barcode-mode** setting. `ComplianceService` + Part fields already done.
- SDS / genealogy / compliance-profile **admin UIs**.

### 3. Stale-doc corrections — not started (owner un-deferred; coordinate)
`kickoff-prompt.md` ("NOT an accounting system") and `ai-system.md` (RAG-only) assert
retired stances (native GL + AI tiers are accepted). The parallel AI effort may be
editing `ai-system.md` — land carefully to avoid a clobber.

## Parallel efforts + coordination
- **forge-db seed refactor** (moving app-seeded C# reference data → forge-db
  `seed//data/`). Owner directive: app-seeded C# moves to forge-db. Handed off the exact
  seed rows: the 13-role `asp_net_roles` insert, the `ComplianceOfficer`
  `calendar_super_group_role_visibilities` grants (guard on role existence + super-group
  `Key`), and the note that `MigrateUserRoleTemplatesToDirectRolesAsync` is a
  migration, NOT seed data. Once forge-db owns the rows, delete the forge-api C#
  (`SeedRoleTemplatesAsync` stays — API-key bundles; the roles list + visibility seeder go).
- **AI fleet effort** — owns ai-fleet (D) topology/ML/provenance + `ai-system.md`.

## Environment notes
- Docker stack is UP (forge-api/ui/db/storage healthy). **`forge-ui` container is stale**
  (~4 days) — my merged UI changes (multi-role admin, `/compliance`) are NOT live until
  `docker compose up -d --build forge-ui`; then screenshot-verify. Did not rebuild
  `forge-api` container to avoid disrupting the parallel backend effort.
- Schema drops reach existing dev DBs only via the forge-db reconciler (SchemaBootstrapper
  no-ops on an existing DB) — see forge-api/CLAUDE.md "Auto-Restart API" note.

## Running inventory (untracked)
The live blocking-questions inventory is `forge/docs/delivery/in-progress/blocking-questions.md`
in the umbrella (not in any git repo in this clone). This handoff is the tracked mirror of
its role/compliance-relevant state.
