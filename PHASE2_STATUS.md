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

> **Update 2026-07-08:** Constraint #1 below is **RESOLVED** — the operational inventory substrate is now wired
> (Armory Plastics hold lifted 2026-06-07). `ReceiveItems.cs` writes `BinContent` + advances PO status;
> `ShipShipment.cs` relieves on-hand + releases the SO-line reservation via `InventoryReliefService` (registered,
> no longer orphaned); the RED remediation tests are now GREEN. Original point-in-time text kept below for history.

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
- **STAGE B — COGS at sale (built, dark):** `InvoiceArPostingService` now posts a separate COGS journal
  on control transfer — Dr COGS / Cr `INVENTORY_FG` at resolved standard cost
  (`Part.ManualCostOverride ?? CurrentCostCalculation.ResultAmount`) for finished-goods lines
  (`InventoryClass.FinishedGood`, non-phantom); service/non-FG/no-cost lines skipped (logged). The engine's
  control-line party guard was made **inventory-aware** (party required for every control account EXCEPT
  inventory — fail-safe, so a null `ControlType` still demands a party) so `Cr INVENTORY_FG` posts
  party-less (reconciled by part via the valuation store). `FinancialStatementService.CogsPosted` is now
  **derived from the ledger** (net COGS activity, window-scoped for the P&L, account-set aware, reversal-
  netting) instead of hardcoded false — so the P&L/BS drop the incomplete-margin caveat once COGS is live.
  §12 decisions baked in + documented: cost source = standard; FG-not-yet-loaded → post-regardless (FG can
  go negative until opening-balance load §7A; valuation-store guard is a STAGE-E refinement); deferred-
  revenue invoices still defer COGS to the (unbuilt) delivery-reclass trigger. 6-lens adversarial review;
  fixes applied. Tests: COGS cases in `InvoiceArPostingServiceTests` + `CogsPosted` derivation/reversal/
  window cases in `FinancialStatementServiceTests`. Full InMemory suite 1252 green.
- **STAGE C — PO receipt (built, dark):** `ReceiptInventoryPostingService`, hooked into `ReceiveItems`
  (transaction-wrapped like Phase-1). On receipt: Dr `INVENTORY_{RAW|WIP|FG}` (per part `InventoryClass` —
  Raw/Component→RAW, Subassembly→WIP, FinishedGood→FG; Consumable/Tool→`OPERATING_EXPENSE`) at **landed
  actual PO cost** (`UnitPrice × qty + allocated freight`) / Cr `GRNI` (base) / Cr `FREIGHT_CLEARING`
  (freight). One JE per receipt, idempotency `Inventory:Receipt:{poId}:{receiptNumber}:RECEIPT`. The engine
  guard (STAGE B) lets the inventory control debit post party-less. 4-lens adversarial review (double-entry
  + engine clean); fixes applied (PO-scoped record query, factory transaction-warning suppression, comment
  accuracy, + a Postgres rollback test). Tests: `Phase2ReceiptPostingServiceTests` (11) +
  `Phase2ReceiptHandlerAtomicityTests` (Postgres). Full InMemory suite **1263 green**.
  - **Decisions:** receipt values inventory at **actual PO price** — the standard-vs-actual + bill-vs-PO
    variance is recognized as **PPV at the STAGE-D 3-way match**, not at receipt. **Consumables/tools are
    expensed** at receipt (no perpetual-supplies key / tool-capitalization signal yet).
  - **Follow-ups before un-darking:** the **second receive path** (`Features/Inventory/ReceivePurchaseOrder`,
    single-line, no `ReceiptNumber`) creates `BinContent` but is **not** GRNI-posted — a stock-without-
    liability asymmetry to converge (mirror-image of "ReceiveItems doesn't create BinContent"). **Freight
    rounding:** Σ `AllocatedFreight` can drift sub-cent from `ActualFreight`, so STAGE D must reconcile
    `FREIGHT_CLEARING` to the freight invoice and route the delta to a variance/rounding account (the
    receipt JE itself always balances — it credits Σ allocated, not `ActualFreight`).
- **STAGE D.1 — 3-way-match foundation (built, dark, additive migration):** added the structural seam the
  match needs — `VendorBillLine.PurchaseOrderLineId` (FK→PO line, nullable) and
  `PurchaseOrderLine.BilledQuantity` (+ computed `UnbilledReceivedQuantity = Received − Billed`), EF configs,
  the `CreateVendorBillLineModel.PurchaseOrderLineId` request field + mapping, and validation (a PO-linked
  bill requires each line to reference a PO line; a standalone bill must not). Migration
  `AddVendorBillPoLineMatch` (2 additive columns + index + FK; no drops — **not applied**). Full InMemory
  suite **1263 green**, no regression. This is structure only — the posting is **D.2** below.

  **STAGE D.2 design (the §12 "3-way-match math", specified for the next build):**
  - **Posting (PO-matched bill, branch in `VendorBillApPostingService` on `PurchaseOrderId != null`):** per
    matched line, `grniClear = billedQty × PO UnitPrice`; `billedAmt = billedQty × bill UnitPrice`;
    `ppv = billedAmt − grniClear`. Post **Dr GRNI** (Σ grniClear) / **Dr|Cr PURCHASE_PRICE_VARIANCE** (net
    ppv; unfavorable bill>PO → Dr) / **Dr OPERATING_EXPENSE** (bill tax) / **Cr AP_CONTROL** (bill Total,
    party=vendor). Balances by construction (Σ grniClear + ppv = Σ billedAmt = subtotal; + tax = Total). One
    entry per bill, idempotency `AP:VendorBill:{id}:BILL` (the match IS the bill posting; standalone bills
    keep the same key — a bill is one or the other).
  - **Partial / multiple bills:** the open GRNI a bill may clear is `UnbilledReceivedQuantity × PO price`.
    The **operational** `ApproveVendorBill` increments `PurchaseOrderLine.BilledQuantity += billedQty`
    (regardless of FULLGL, after the posting reads the pre-bill value), so a second bill can't double-clear.
  - **Bill-before-receipt / over-bill (§7 ordering — fail-and-surface):** if `billedQty >
    UnbilledReceivedQuantity` (billing more than received-not-yet-billed → no GRNI accrued), the posting
    throws `PostingException("GRNI_INSUFFICIENT")` and the whole approval rolls back, rather than driving
    GRNI negative. (An operational pre-check should mirror this so a *dark* over-bill is also blocked — a
    D.2 item.)
  - **Deferred to D.3 (reporting):** the **GRNI aging report** (group GRNI lines by `JournalEntry.SourceId`
    = PO id where `Source=Inventory, SourceType="Receipt"`, age by entry date, credit-positive — note the
    match's GRNI debit currently posts under the bill's `AP` source, so per-PO netting in the aging needs
    the match to tag its GRNI-clear leg with the receipt's `Inventory:Receipt:{poId}` source, OR the aging
    to net across both sources by PO — decide at D.3) and the **line-level ReceivingRecord↔GRNI
    reconciliation** sweeper (§12 — must check line-level coverage, not `(SourceType,SourceId)` presence).

- **STAGE D.2 — 3-way-match posting (BUILT, dark, commit pending):** implemented exactly to the design above.
  - `VendorBillApPostingService.PostCoreAsync` now branches on `bill.PurchaseOrderId`: `BuildStandaloneDebits`
    (unchanged STAGE-A path) vs **`BuildPoMatchedDebits`** — per line **Dr GRNI** = `Quantity × PO UnitPrice`
    (one granular line each, keyed for D.3 line-level reconciliation), net **Dr|Cr PURCHASE_PRICE_VARIANCE** =
    `Σ(LineTotal − grniClear)` (Dr unfavorable / Cr favorable), then the shared **Dr OPERATING_EXPENSE** (tax)
    / **Cr AP_CONTROL** (Total, party=vendor). Balances by construction. Bill load extended to
    `.Include(b => b.Lines).ThenInclude(l => l.PurchaseOrderLine)`; `VendorBillRepository.FindWithDetailsAsync`
    likewise (for the handler).
  - **`ApproveVendorBill`** loads via `FindWithDetailsAsync`, runs the **operational** 3-way-match guard
    (regardless of FULLGL): rejects the PO↔line **invariant** violations (PO-matched bill with an unlinked
    line / standalone bill with a linked line), the **cumulative** over-bill (GroupBy PO line, `Σqty >
    UnbilledReceivedQuantity` → throw), and **after** the posting reads the pre-bill value, increments
    `PurchaseOrderLine.BilledQuantity += Σqty` — all inside the one `BeginTransaction`/`Commit`. A posting
    failure leaves the bill Draft and `BilledQuantity` unchanged.
  - **Zero-priced received line** (valid: `UnitPrice ≥ 0`, `Quantity > 0`) is *not* skipped — it still clears
    its GRNI (Dr GRNI at PO price / Cr PPV favorable), or the GRNI accrual would be stranded while
    `BilledQuantity` advanced. Skip is on `Quantity ≤ 0` only. (Self-caught; regression-tested.)
  - Tests: **+18** (service: exact / unfavorable-PPV / favorable-PPV / mixed-zero-priced / two-lines-same-
    PO-PPV-accumulation / zero-PO-price / tax / over-bill / partial-remainder / idempotent; handler: FULLGL
    on+off advance, over-bill, second-bill, cumulative-two-line over-bill, invariant ×2, posting-failure
    rollback). Full InMemory suite **1281 green**, 0 failed, 7 skipped (the 3 Docker PG `…AtomicityTests`
    still unrun — sandbox has no Docker).
  - **Adversarial verify (4-lens workflow):** balance lens **0 findings** (arithmetic proven); ordering/
    atomicity/dark-gating **confirmed correct by design**. Fixed pre-commit: the PO↔line invariant re-check
    in `ApproveVendorBill` (a `PurchaseOrderId`-null / line-linked mismatch would have silently routed a PO
    line through the standalone path → expense instead of GRNI clear), and the service over-bill guard made
    **cumulative** (was per-line — two lines on one PO line could each pass yet over-clear if `PostAsync` were
    called directly). Both now match the handler.
  - **Deferred (pre-go-live hardening, tracked below):** lost-update concurrency on `BilledQuantity` (two
    concurrent same-PO-line approvals under READ COMMITTED can both pass + both increment); and a Postgres
    proof that a posting failure *after* the engine's SaveChanges rolls back the `BilledQuantity`/status.

- **STAGE D.3 — GRNI reconciliation + aging (BUILT, dark, commit pending):** read-only — derives everything,
  mutates nothing. New `GrniReconciliationService` + `IGrniReconciliationService` + `GetGrniReconciliationQuery`
  + `GET /api/v1/accounting/grni-reconciliation` (gated `CAP-ACCT-FULLGL`, Controller-role) + DI. Model
  `GrniReconciliation`.
  - **GL balance** = net of the `GRNI`-key account(s), credit-positive (Cr − Dr), Posted + Reversed,
    `EntryDate ≤ AsOf` (excludes `FREIGHT_CLEARING` — freight is a separate key). **Operational open** =
    Σ `UnbilledReceivedQuantity × PO UnitPrice` over open PO lines. **Variance** = GL − operational, the §12
    control; `IsReconciled` absorbs sub-cent residue within the **book `RoundingTolerance`** (GL is posted at
    currency scale; operational is computed fresh — fractional qty×price would otherwise false-fail an exact 0).
  - **Aging** buckets the open amount per PO by each line's *earliest* receipt date (`ReceivingRecord.CreatedAt`),
    same 0-30/31-60/61-90/91+ grain as AP/AR. **Uncovered-receipts sweep** (line-level coverage): open-line
    receiving records with no GRNI accrual JE (matched by the receipt idempotency key
    `Inventory:Receipt:{poId}:{receiptNumber}:RECEIPT`); blank receipt number → `NO_RECEIPT_NUMBER`; bounded
    to 200 + `UncoveredTruncated`.
  - **2-lens adversarial verify:** the lone "critical" (DayNumber year-boundary) was a **false positive** —
    `DateOnly.DayNumber` is days-since-0001 (epoch), not day-of-year; `ApAgingService` uses the same pattern.
    Added `Aging_ReceiptInPriorYear_BucketsByActualDayCount` which **passes**, proving it. Fixed: blank
    (whitespace) receipt number now reports `NO_RECEIPT_NUMBER` (was `NO_ACCRUAL_POSTED`); zero-priced open
    lines no longer falsely flagged uncovered (no GRNI to cover). +10 tests; full suite **1291 green**.
  - **Documented caveats (ratify):** operational side uses *current* qty *and PO unit price* (a price edit
    after receipt legitimately shows as variance); earliest-date aging (not per-receipt FIFO); uncovered
    sweep scoped to open lines (a fully-billed-line gap still shows in the exhaustive `Variance`); a reversed
    accrual reads as variance, not uncovered (cross-reference the ledger). No migration (read-only).
- **E:** STAGE E (inventory valuation store: standard / weighted-avg / FIFO, §8.1).

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

**Pre-go-live hardening — DONE (commits H1 `c4ddacbc`, H2 `8d9e392d`, H3 this commit):**
- **Duplicate-vendor-invoice (double-payment) protection — DONE (H1).** `CreateVendorBill` rejects a
  `(VendorId, VendorInvoiceNumber)` that already exists (null/blank exempt); partial unique index
  `ux_vendor_bills_vendor_invoice` as the DB backstop (additive migration, not applied).
- **AP void/correction path — DONE (H2).** `VoidVendorBill` (+ `POST /vendor-bills/{id}/void`): a Draft bill
  cancels; an Approved bill **reverses** the AP journal (engine `ReverseAsync`) and hands `BilledQuantity`
  back to its PO lines, in one tx; blocked when payments are applied. (Client `If-Match` concurrency on AP
  mutations still open — lower priority; `VendorBill.Version` token already exists.)
- **3-way-match concurrency — DONE (H3).** `ApproveVendorBill` now takes a `FOR UPDATE` row lock on the
  matched `purchase_order_lines` (Postgres only; no-op on InMemory) inside the transaction, before the
  over-bill read + `BilledQuantity` increment, so concurrent same-line approvals serialize (no lost-update /
  double-clear). The chosen lock (vs an EF concurrency token) does **not** alter update behavior on the
  operational table for other flows — safe to carry while Armory Plastics tests. Still open: a Postgres
  proof that a posting failure after the engine's SaveChanges rolls back `BilledQuantity`/status (run with
  the deferred `Phase…AtomicityTests` on a Docker box before un-darking).
- **AR-side parity — DONE (owner-approved follow-up).** The review found the same gaps in the Phase-1
  `CreatePayment`; the four guards are now mirrored to it: `Method` enum validation (400 not 500),
  duplicate-invoice-application rejection, customer-ownership of the applied invoice, and a status guard
  (only Sent/PartiallyPaid/Overdue invoices payable — a Draft invoice's AR debit isn't booked until
  `SendInvoice`). Added `CreatePaymentHandlerTests` for all four. Existing AR tests pay Sent invoices, so
  no regression (full suite 1244 green).

*Generated for human review of the autonomous Phase-2 build. `CAP-ACCT-FULLGL` remains OFF; nothing deployed;
no migration applied. The inventory/COGS stages wait on the operational substrate (don't perturb Armory
Plastics' testing) + the ratify-items above.*
