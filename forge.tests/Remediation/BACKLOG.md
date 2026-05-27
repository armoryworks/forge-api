# Remediation backlog (audit findings → tests → status)

The **api-layer execution slice** of the audit remediation. The 44-phase audit is
complete; the deduplicated, cross-layer **master catalog** (all layers, all
severities, grouped by root cause + the burn-down waves) lives in the product repo
at `docs/delivery/in-progress/audit-remediation/findings-catalog.md`. This file is
the live `grep "Skip = \"RED"` tracker for the findings whose test is xUnit / EF
`TestDbContextFactory` / `WebApplicationFactory` (the api defects). UI findings
(Vitest / Cypress / axe) are tracked in `forge-ui` and the master catalog.

Status: `☐` todo · `🔴` RED test written (skipped, awaiting fix) · `✅` green (fixed + test passing)

| ID | Sev | Area / layer | Defect (where) | Expected (definition-of-correct) | Test | Status |
|----|-----|--------------|----------------|----------------------------------|------|--------|
| AUDIT-21-S1 | **BLOCKER** | Invoices/Payments · api | AR invoices & payments never enqueue the QBO `SyncQueue`; only `MoveJobStage.cs:172` enqueues | Creating an invoice/payment in standalone+integrated mode enqueues a QBO sync row | xUnit handler (`CreateInvoice`/`CreatePayment`) | ☐ |
| AUDIT-S4 / BE20-C | **HIGH** | Quotes · api | `ConvertQuoteToOrder.cs:27-48` converts a zero-line quote into a live, confirmable order | An empty quote cannot convert (throws) | `Quotes/ConvertQuoteToOrderRemediationTests` | 🔴 |
| AUDIT-S6 / BE18-1 | **HIGH** | Leads · api | `ConvertLead.cs` split `SaveChanges`, no transaction → orphan customer on partial failure | Lead→customer convert is atomic (one transaction; rolls back on failure) | xUnit handler + `TestDbContextFactory` | ☐ |
| AUDIT-P06-1 / Q2C-BE-8 | **HIGH** | Invoices · api | `CreateInvoice.cs:49-99` does not enforce `invoiced ≤ shipped` | Cannot invoice more than has shipped (validation rejects) | xUnit handler | ☐ |
| AUDIT-P06-3 / INV-1 | **HIGH** | Shipments/Inventory · api | Shipping does not relieve on-hand; `InventoryReliefService` orphaned (`Program.cs:387`) | Shipping a line decrements bin on-hand | xUnit handler / integration | ☐ |
| AUDIT-19-S1 | **HIGH** | Quotes pricing · api | Customer price lists are a dead input to quote line pricing | Quote line price resolves from the customer's price list when present | xUnit handler | ☐ |
| AUDIT-V9 | **HIGH** | Vendors · api | Vendor price-tier variance silently dropped | Vendor-part price-tier writes persist / surface, no silent drop | xUnit handler | ☐ |
| AUDIT-D5 | **HIGH** | Parts/BOM · api | No BOM cycle guard (A→B→A possible) | Adding a BOM edge that forms a cycle is rejected | xUnit handler + `TestDbContextFactory` | ✅ |
| AUDIT-BE-1 (Q-3/SO-8) | **HIGH** | Quotes/SalesOrders · api+ui | Quote lines & SO header/lines immutable after creation; no edit path | Draft quotes/orders are editable (header + lines) | xUnit handler (api) + Vitest/Cypress (ui) | ☐ |
| AUDIT-S3 | **MED** | Quotes · api | `ConvertQuoteToOrder.cs:27-34` drops `quote.Notes` | Convert preserves `Notes` onto the order | `Quotes/ConvertQuoteToOrderRemediationTests` | 🔴 |
| AUDIT-S3b / SO-8 | **MED** | SalesOrders · ui | SO-only header fields (CreditTerms/BillingAddress/RequestedDelivery/CustomerPO) can't be set post-convert (SO-edit dead) | Draft SO header is editable for these fields | Cypress E2E (ui) | ☐ |
| BE-1 (carried) | **HIGH** | Calendars · api | `working-calendars/:id/set-default` → HTTP 500 (non-atomic default swap; unique `is_default` violation) | Set-default atomically clears the prior default (no 500) | xUnit handler + `TestDbContextFactory` | ☐ |
| G-MFA-3 | **BLOCKER** | MFA · api | TOTP HMAC keyed on `UTF8.GetBytes(secret)` vs the base32 QR secret → authenticator codes never match; QR enrolment broken | Base32-decode the secret before HMAC; QR + validation agree (golden-vector test) | xUnit handler | ☐ |
| G-38-MRP-3 / F-07B-03 | **BLOCKER** | Planning · api | `PlanningCyclesController` mutations reachable by ProductionWorker — no role gate (live POST→201) | `[Authorize(Roles="Admin,Manager")]` on all planning-cycle mutations | `WebApplicationFactory` integration | ✅ |
| F-EXP-01 | **BLOCKER** | Expenses · api | `PATCH /expenses/{id}/status` has no role/self gate — any user approves any expense (live) | Approval gated by role/ownership; routed through `ApprovalService` | `WebApplicationFactory` integration | ✅ (role gate; `ApprovalService` routing = F-26B-05) |
| S-MV1 | **HIGH** | Shipments/Inventory · api | `ShipShipment` leaks on two axes: never relieves `on_hand` AND never releases the SO-line reservation (sharpens AUDIT-P06-3) | Ship decrements `BinContent` **and** releases the `SalesOrderLineId` reservation | xUnit + `TestDbContextFactory` | ☐ |
| S-RI1 | **HIGH** | Inventory · api | `TransferStock`/`AdjustStock`/`UpdateCycleCount`/`RemoveBinContent` ignore `ReservedQuantity`, inflating `available` | Reducing/removing a bin throws if `newQty < ReservedQuantity`; transfer carries reserved to dest | `TestDbContextFactory` | ✅ AdjustStock (Transfer/CycleCount/Remove still owed) |
| PRI-1 / PRI-2 / PRI-3 | **HIGH** | Purchasing/Inventory · api | PO-side ReceiveDialog marks PO Received + signals "Materials Ready" but writes no `BinContent`; inv-tab Receive stocks but never advances PO status (notify-XOR-stock) | One receive path both writes `BinContent` and advances PO status; location required when stocking | xUnit + `TestDbContextFactory` | ☐ |
| F-JQ1 | **HIGH** | Jobs/Quality · api | Job advances through completion with open NCRs / failed inspections / unresolved CAPAs | `MoveJobStage` rejects advance when `NCR.Status==Open` or `QcInspection.Status==Failed` | xUnit handler | ☐ |
| F-26B-01 | **HIGH** | Expenses · api+db | Expense has no vendor/payee link full-stack (no FK, API, or UI field) | Add `VendorId`/`PayeeId` FK to `Expense`; vendor picker on create | `WebApplicationFactory` integration | ☐ |
| F-26B-02 | **HIGH** | Expenses/QBO · api | Expense→QBO posts as a vendorless cash purchase (no `VendorRef`); invisible in vendor aging | Set `VendorExternalId` on the accounting expense from the vendor FK | `WebApplicationFactory` integration | ☐ |
| F-26B-05 | **HIGH** | Expenses · api | Configured multi-step approval policy bypassed — single status-flip ignores routing/approvers/escalation | Expense approval goes through `ApprovalService`, not a direct status PATCH | `WebApplicationFactory` integration | ☐ |
| G-39-EMAIL-1 | MED | Comms · api | `GET /communications/connections` returns 200 when `CAP-EXT-EMAIL-SYNC` OFF (read-leak; cap-check is mutations-only) | Add `[RequiresCapability]` to the GET (or register the `communications` prefix) | `WebApplicationFactory` integration | ☐ |

## Notes

- **AUDIT-S3 was sharpened by writing the test:** the audit said "5 header fields
  dropped on convert," but `Quote.cs` only carries `Notes` of the five — the other
  four are SalesOrder-only with no quote source (split into `AUDIT-S3b/SO-8`, a UI
  gap). The convert-bug is the single `Notes` drop.
- Rows marked `🔴` have a written test in this suite (skipped until the fix lands).
- **Fold-in complete (2026-05-27):** the flow-tier (27–30), intersections (23–26b),
  and gating (36–40) api findings are now in the table above (S-MV1, S-RI1, PRI-*,
  F-JQ1, F-26B-*, G-MFA-3, G-38-MRP-3, F-EXP-01, G-39-EMAIL-1). UI/UX, WCAG, and
  cap-gating-coherence findings live in the master catalog (they're Vitest/Cypress/
  axe, not xUnit) — see `docs/delivery/in-progress/audit-remediation/`.

## Burndown — GREEN so far (2026-05-27)

Ship-gate **authz cluster** fixed + tests passing (`dotnet test` 8 passed / 0 failed):
- **K-F13 / K-F15 / K-F14** — JobsController explode-bom / PUT (reassign) / dispose now
  carry `[Authorize(Roles="Admin,Manager")]` (controller-level grant + method gate = AND).
- **SF-04 / SF-05** — ShopFloorController complete-job / assign-job gated to Admin/Manager.
- **P-F6 / G-38-MRP-3** — every PlanningCyclesController mutation gated to Admin/Manager (reads stay open).
- **F-EXP-01** — Expenses status PATCH gated to Admin/Manager/OfficeManager (the deeper
  `ApprovalService` routing + self-approval block remain as F-26B-05 / F-EXP-04).
- **F-13-CAP-04** — `GET /capabilities/{id}/relations` now Admin-only (matched its siblings).

**Data-integrity** batch fixed + tests passing (`dotnet test` 10 passed / 0 failed):
- **AUDIT-D5** — `CreateBOMEntry` now rejects any edge that closes a BOM cycle (BFS the
  child's descendants for the parent), via a new `IPartRepository.GetBomChildIdsAsync`.
- **S-RI1** — `AdjustStock` rejects dropping a bin below its `ReservedQuantity` (→ 409).
  The same guard is still owed on `TransferStock` / `UpdateCycleCount` (approve) / `RemoveBinContent`.

Next burndown targets: handler-ownership checks (F-EXP-06 delete, TT-01 delete; need the
caller's roles plumbed into the handler), then the remaining S-RI1 surfaces.

## RED test coverage landed (2026-05-27)

42 RED tests across 29 files in `Remediation/<Feature>/` now encode the
definition-of-correct for the api-layer findings of Regions 1–5. All are
`[Fact(Skip="RED: …")]` and the suite builds green (`dotnet build -warnaserror`).
The live remaining-work list is `grep -rn 'Skip = "RED' forge.tests/Remediation`.

| Region | Feature files (findings) |
|--------|--------------------------|
| 1 Master Data | Leads (L3, C1-back) · Customers (C8, C2, C3) · Vendors (V9) · Parts (D5, D2b) · Inventory (S-RI1, S1, S2a) · Lots (L2) |
| 2 Quote-to-Cash + Expenses | Quotes (BE-1) · Estimates (BE-3, E-1) · SalesOrders (BE-1) · RecurringOrders (BE-4) · PurchaseOrders (P06-4, PRI-1/2/3) · Shipments (P06-3/S-MV1) · Invoices (AUDIT-21-S1) · Payments (P06-5) · Expenses (F-EXP-01, F-EXP-06, F-26B-01, F-EXP-03) · Quotes-convert (AUDIT-S3, S4 — pre-existing) |
| 3 Operations | Kanban (K-F13/F15/F14) · ShopFloor (SF-04/05) · TimeTracking (TT-01) · Assets (AS-01, AS-03) · Planning (P-F6) · MRP (MRP-03) |
| 4 Platform | Approvals (F-11-APPR-02) |
| 5 Admin + Account | Capabilities (F-13-CAP-04) · Training (F-14-BE-01) · Announcements (F-13-ANN-01) |

### Deferred — NOT yet covered by an api RED test (why)

- **Postgres-specific** (InMemory can't reproduce the filtered-unique-index race): BE-1 / F-12-BE-01 working-calendar set-default 500, F-12-BE-02 CompanyLocation set-default, F-14-BE-02 OvertimeRule IsDefault. Need a real-Postgres (Testcontainers) integration harness.
- **Crypto / entity-shape**: G-MFA-3 (TOTP base32 — needs a golden-vector unit test + OtpNet or manual HMAC in the test project), F-13-MFA-01 (MfaPolicy entity), F-12-USR-01 (ApplicationUser.ManagerId).
- **Complex multi-entity seeding**: F-JQ1 (job advance past open NCR — needs stage graph), F-12-AUDIT-01 (approval audit — needs a pending step + approver), AUDIT-P06-1 (invoiced≤shipped — needs shipment context), AUDIT-19-S1 (price-list pricing).
- **Other api**: AUDIT-S6 (ConvertLead atomicity), F-26B-02/05 (expense→QBO vendor / approval routing), G-39-EMAIL-1 (cap read-leak), Quality Q-03, Worker W-01, OEE-01/02.
- **Stale at api level** (already implemented; UI/executor gap only): F-10-RPT-02/03 (report export + schedules exist), F-13-BE-01 (EDI mapping CRUD exists), P06-11c (customer-return DELETE exists).
- **UI layer** (Vitest/Cypress/axe — out of this .NET run; tracked in the master catalog for a forge-ui spec pass): all of Regions 6 (Auth/MFA-login-contract/Onboarding/Portal/Mobile/AI) and 7 (nav cap-coherence, shared-component spine, WCAG), plus the UI rows within Regions 1–5 (C7/C5 tab caps, S2c bin pickers, P-F1, etc.).
- **Burn-down is feature-by-feature** (master catalog spine, 2026-05-27 directive):
  take a feature to completion — write RED tests for all its rows, fix
  blocker→high→med→low, ship — rather than a severity skim. The **ship gate**
  (G-MFA-3, F-EXP-01, the kanban/shop-floor/planning/time-tracking authz blockers,
  F-14-BE-02, working-calendar 500) must be GREEN before GA regardless of feature
  order. This table is the api slice of the catalog; rows here are grouped loosely
  by severity for the `grep "Skip = \"RED"` view, but execution follows the feature
  order in `docs/delivery/in-progress/audit-remediation/`.
