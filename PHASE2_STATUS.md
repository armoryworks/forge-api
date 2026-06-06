# Accounting GL — Phase 2 Entry Design + Build Status (for human review / ratification)

**Branch:** `feat/accounting-gl-phase1` (continuing on the same branch; will rename/split if desired)
**Plan reference:** `../ACCOUNTING_SUITE_PLAN.md` §6 (Phase-2 row), §7 (posting matrix Phase-2 rows),
§8.1 (costing), §12 (Phase-2 "specify at phase entry")
**Date:** 2026-06-05
**Capability state:** `CAP-ACCT-FULLGL` remains **OFF**. All Phase-2 code is **DARK & NON-REGRESSING**,
**branch-only, no migration applied, no deploy** — same guardrails as Phase 0/1.

> **Why this doc exists:** Plan §12 explicitly defers Phase-2 "detailed mechanics" to phase entry. This
> records the design decisions (with **manufacturing-first defaults, flagged for owner/accountant
> ratification**), the staged build, and two hard constraints discovered during scoping. The owner asked me
> to proceed autonomously into Phase 2; this is where I'm starting and why.

---

## Two constraints that shape Phase 2 scope

1. **Operational inventory substrate is INCOMPLETE and must not be perturbed yet.** The flows Phase-2
   COGS/inventory posting would hook into are not finished operationally:
   - `Features/PurchaseOrders/ReceiveItems.cs` creates `ReceivingRecord`s but **does not create `BinContent`**
     (no stock-on-hand bump) — RED remediation test `Receiving_a_PO_line_creates_stock` (skipped).
   - `Features/Shipments/ShipShipment.cs` flips status but **does not relieve inventory**;
     `Services/InventoryReliefService.cs` exists but is **orphaned** (not wired) — RED remediation test
     `Shipping_relieves_on_hand_inventory` (skipped).
   Wiring those is **core operational behavior change** (shipments would start decrementing stock / could
   throw "insufficient stock"). **Armory Plastics is now testing on their box**, so I will not change that
   operational behavior here. Phase-2 inventory/COGS **posting** can be built dark and hooked at those sites
   on the branch (no deploy), but it only becomes *semantically* correct once the operational relief/receipt
   is wired — so the inventory/COGS stages are **sequenced after** that operational work + ratification.

2. **Real-Postgres verification needs Docker** (the InMemory provider ignores transactions, can't model the
   filtered-unique-index / `ExecuteUpdate` / trigger paths). The authoring sandbox currently has **no Docker
   access** (see PHASE1_STATUS "STAGE F"). InMemory service tests DO cover posting *logic* (this is how the
   Phase-1 posting services are tested). Transaction-rollback proofs (Postgres) are written but must be run
   where Docker is available.

**Net:** start with the **AP sub-ledger** (net-new entities, zero operational perturbation, a 1:1 mirror of
the proven Phase-1 AR side, InMemory-verifiable). Defer inventory/COGS/3-way-match to later stages that need
the operational substrate + ratification.

---

## Staged Phase-2 build plan (each dark / gated / branch-only)

| Stage | Scope | Operational risk | Verifiable here? |
|---|---|---|---|
| **A — AP sub-ledger** | `VendorBill(Line)`, `VendorPayment(Application)`; AP posting (bill → Dr expense/Cr AP; payment → Dr AP/Cr Cash); AP aging | **None** (net-new entities) | Yes (InMemory service tests) |
| **B — COGS at sale** | `CogsPostingService` hooked at shipment: Dr COGS / Cr `INVENTORY_FG` at per-unit cost; flips `CogsPosted` → unblocks `CAP-RPT-FINANCIALS` | Reads ship lines; needs op relief wired to be *semantically* correct | Logic yes; needs FG-edge ratify |
| **C — PO receipt** | Dr Inventory(+freight) / Cr `GRNI` / Cr `FREIGHT_CLEARING` at receipt; retire double-posted `FreightAllocatedEvent` | Hooks `ReceiveItems`; needs op BinContent wired | Logic yes |
| **D — 3-way match + PPV** | `VendorBill` matched to PO/receipt: Dr GRNI / Cr AP, price/qty diff → `PURCHASE_PRICE_VARIANCE`; GRNI aging + line-level reconciliation | Builds on A + C | Logic yes |
| **E — valuation store** | per-method valuation entity (§8.1): standard / weighted-avg / FIFO; cost source for B/C/D | Schema design (ratify) | Yes |

**STAGE A is the focus now.** B–E are specified below for ratification, then built in sequence.

---

## Design decisions — proposed defaults (flag for ratification)

### STAGE A — AP sub-ledger (building now)

**Entities (mirror the AR side exactly):**
- `VendorBill : BaseAuditableEntity, IConcurrencyVersioned` — `BillNumber` (our ref, unique),
  `VendorId`, `VendorInvoiceNumber?` (the vendor's doc #), `Status` (`VendorBillStatus`), `BillDate`,
  `DueDate`, `CreditTerms?`, `TaxAmount` (stored — vendor tax is a given amount, not our rate), `Notes?`,
  `PurchaseOrderId?` (**nullable** — standalone bills are null; the link is the seam for STAGE-D 3-way
  match), accounting-integration fields. Computed `Subtotal/Total/AmountPaid/BalanceDue` (mirror `Invoice`).
- `VendorBillLine : BaseEntity` — `VendorBillId`, `PartId?`, `Description`, `Quantity`, `UnitPrice`,
  `LineNumber`, `AccountDeterminationKey` (**the GL key this line debits; default `OPERATING_EXPENSE`**).
  > **Ratify:** lines carry a determination *key* (loose coupling, matches the Phase-1 services + §5.1
  > "never hardcode account numbers"), not a direct `GlAccountId`. Inventory bills (STAGE D) will use
  > `GRNI`/`INVENTORY_*` keys. A per-line free GL-account picker can be added later if the accountant wants it.
- `VendorPayment : BaseAuditableEntity, IConcurrencyVersioned` — mirror `Payment` (`PaymentNumber`,
  `VendorId`, `Method`, `Amount`, `PaymentDate`, `ReferenceNumber?`, `Notes?`, applications,
  `AppliedAmount/UnappliedAmount`).
- `VendorPaymentApplication : BaseEntity` — `VendorPaymentId`, `VendorBillId`, `Amount` (mirror
  `PaymentApplication`).

**Posting (dark, gated, atomicity-wrapped from the start):**
- **VendorBill approved** → for each line `Dr <line.AccountDeterminationKey>` (amount = line total + its tax
  share) / **`Cr AP_CONTROL`** (party = vendor) for the bill total. Idempotency key
  `AP:VendorBill:{id}:BILL`. (Standalone bill; the PO-matched GRNI-clearing variant is STAGE D.)
- **VendorPayment created** → **`Dr AP_CONTROL`** (party = vendor, applied amount) / `Cr CASH` (amount);
  unapplied → a vendor prepayment/advance (key `PREPAID_EXPENSE` as the debit-side advance, or hold as
  unapplied on AP — **ratify**; default: post only the applied portion to AP, leave unapplied as a payment
  on account, mirroring the customer-deposit treatment but on the asset side). Idempotency
  `AP:VendorPayment:{id}:PAYMENT`.
- Both handlers wrap operational write + posting in **one `BeginTransactionAsync`/`CommitAsync`** (the
  Phase-1 STAGE-F atomicity lesson, baked in from day one).

**AP aging** — `ApAgingService` mirroring `ArAgingService`: age the `AP_CONTROL` balance by vendor party
from posted journal lines, bucketed by `VendorBill.DueDate` (current / 1-30 / 31-60 / 61-90 / 90+).
Filter-immune projection like the trial balance. Endpoint `GET /api/v1/accounting/ap-aging`, dual-gated
(`CAP-ACCT-FULLGL` + `CAP-RPT-FINANCIALS`).

### STAGE B — COGS at sale (specify, build after op-relief wired)
- **Cost source** (standard-costing default, §8.1): `Part.ManualCostOverride ?? CostCalculation.ResultAmount`
  via the active `CurrentCostCalculationId`; **fallback when both null** → log + post at 0 with a flagged
  `MarginCaveat` (do NOT block the sale). **Ratify** the fallback (0 vs. last-PO-price vs. block).
- **FG-not-yet-loaded edge (§12 Phase-1 item, resolved here):** **defer COGS for production-sourced goods
  until Phase-2 valuation is live; stocked/purchased goods relieve normally.** Gate COGS-at-sale on FG
  availability so we never drive `INVENTORY_FG` negative. **Ratify.**

### STAGE C — PO receipt (specify)
- `Dr INVENTORY_{RAW|WIP|FG}` (per `Part.ValuationClassId`; default RAW for purchased parts) + allocated
  freight → same inventory debit (landed) / `Cr GRNI` (base) / `Cr FREIGHT_CLEARING` (freight). Retire the
  pre-existing `FreightAllocatedEvent` posting so freight isn't double-counted (§7).

### STAGE D — 3-way match + PPV (specify)
- On VendorBill matched to a PO/receipt: `Dr GRNI` (received qty × PO price) / `Cr AP` (billed) ; difference
  (`billed − PO/standard`) × qty → `PURCHASE_PRICE_VARIANCE`. GRNI clears to 0 when fully billed. Handle
  bill-before-receipt (accrue) and partial-receipt/partial-bill; **GRNI aging** + **line-level**
  ReceivingRecord↔GRNI reconciliation (not just `(SourceType, SourceId)` presence). §12 Phase-2 item.

### STAGE E — valuation store (specify, §8.1)
- `Standard`: reuse `CostCalculation`/`CostingProfile` + variance accounts (no new on-hand store; PPV/MUV
  carry the variance). `WeightedAverage`: one `(BookId, PartId)` row (on-hand qty + moving unit cost),
  recomputed each receipt. `FIFO`: cost-layer rows `(BookId, PartId, receiptRef, qtyRemaining, unitCost)`.
  Product default **Standard**; per-tenant ratifiable. §12 Phase-2 item.

---

## Build status

- **STAGE A.1 (commit `bedb4be9`):** AP sub-ledger entities + EF configs + DbSets + additive migration.
- **STAGE A.2 (commit `4497a344`):** the two AP posting services + DI + 8 InMemory service tests.
- **STAGE A.3 (this commit):** `VendorBill`/`VendorPayment` repositories; `CreateVendorBill` /
  `ApproveVendorBill` / `CreateVendorPayment` handlers (atomicity-wrapped, mirroring the fixed Phase-1
  handlers); GET list/by-id queries; `VendorBillsController` / `VendorPaymentsController`; `ApAgingService`
  + `GetApAging` + `ap-aging` endpoint; DI. Tests: AP aging (9), AP handler flow (9), Postgres rollback (3).
  Full InMemory suite green (1244 passed, 0 failed). Reviewed by a 6-lens adversarial pass (double-entry /
  atomicity / dark-gating / mirror-fidelity / state-machine / completeness) — fixes below applied.
- **B–E:** sequenced per the table above; B/C require the operational inventory wiring + the ratify-items.

### STAGE A.3 review — fixes applied + follow-ups

Fixed in this commit (from the adversarial review):
- **Pay only a booked payable** — `CreateVendorPayment` now rejects applying to a non-`Approved`/
  -`PartiallyPaid` bill (a Draft bill's AP credit isn't posted until approval; paying it would Dr AP against
  an unrecorded liability and drive AP-control negative). Closes the pay-Draft and pay-Void holes.
- **Vendor-ownership guard** — the payment's vendor must own each applied bill.
- **`Method` validation** — invalid `PaymentMethod` is now a 400 (was a 500 from `Enum.Parse`).
- **Duplicate-application guard** — a bill may be referenced at most once per payment (the per-bill
  over-apply check sees the same tracked balance otherwise).
- **Zero-total bill rejected** — would otherwise approve yet post no journal (silent divergence).

**Capability taxonomy (ratify):** the two AP controllers gate on **`CAP-P2P-PO`** (the default-on baseline
"every shop with vendors uses this"), not the receiving-specific `CAP-P2P-RECEIVE`. The correct end-state,
symmetric to the AR side's `CAP-O2C-INVOICE` / `CAP-O2C-CASH`, is **dedicated `CAP-P2P-BILL` / `CAP-P2P-PAY`**
catalog entries — deferred because new capabilities are a product-taxonomy decision for the owner.

**Pre-go-live follow-ups (tracked, not blocking the dark increment):**
- **No void/cancel/correction path** and no client `If-Match` concurrency on AP mutations — the AP
  sub-ledger is append-only via this API. A void-with-reversal flow is needed before un-darking.
- **Duplicate-vendor-invoice (double-payment) protection** — no uniqueness on `(VendorId,
  VendorInvoiceNumber)`; the highest-value AP control to add before go-live (AR doesn't need this — we
  issue invoices, we receive bills).
- **AR-side parity** — the review found the same `Enum.Parse`→500 and duplicate-application gaps in the
  Phase-1 `CreatePayment` (and no status/vendor guards). Fixed here on AP; **recommend mirroring the fixes
  to `CreatePayment`/`SendInvoice`** in a separate change (not done here to avoid mutating committed,
  tested Phase-1 AR code without owner sign-off).

*Generated for human review of the autonomous Phase-2 build. `CAP-ACCT-FULLGL` remains OFF; nothing deployed;
no migration applied. The inventory/COGS stages wait on the operational substrate (don't perturb Armory
Plastics' testing) + the ratify-items above.*
