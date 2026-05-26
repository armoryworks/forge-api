# Remediation backlog (audit findings → tests → status)

Prioritized bridge from the `forge-analysis` audit to the TDD burn-down. Seeded
from the **completed + verified** audit phases (completeness 02–16, intersections
18–22). It will be **expanded from the phase-41 coverage merge** once the audit
finishes — that merge is the deduped source of truth; treat this as the starting
slice, HIGH-severity first.

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
| AUDIT-D5 | **HIGH** | Parts/BOM · api | No BOM cycle guard (A→B→A possible) | Adding a BOM edge that forms a cycle is rejected | xUnit handler + `TestDbContextFactory` | ☐ |
| AUDIT-BE-1 (Q-3/SO-8) | **HIGH** | Quotes/SalesOrders · api+ui | Quote lines & SO header/lines immutable after creation; no edit path | Draft quotes/orders are editable (header + lines) | xUnit handler (api) + Vitest/Cypress (ui) | ☐ |
| AUDIT-S3 | **MED** | Quotes · api | `ConvertQuoteToOrder.cs:27-34` drops `quote.Notes` | Convert preserves `Notes` onto the order | `Quotes/ConvertQuoteToOrderRemediationTests` | 🔴 |
| AUDIT-S3b / SO-8 | **MED** | SalesOrders · ui | SO-only header fields (CreditTerms/BillingAddress/RequestedDelivery/CustomerPO) can't be set post-convert (SO-edit dead) | Draft SO header is editable for these fields | Cypress E2E (ui) | ☐ |
| BE-1 (carried) | **HIGH** | Calendars · api | `working-calendars/:id/set-default` → HTTP 500 (non-atomic default swap; unique `is_default` violation) | Set-default atomically clears the prior default (no 500) | xUnit handler + `TestDbContextFactory` | ☐ |

## Notes

- **AUDIT-S3 was sharpened by writing the test:** the audit said "5 header fields
  dropped on convert," but `Quote.cs` only carries `Notes` of the five — the other
  four are SalesOrder-only with no quote source (split into `AUDIT-S3b/SO-8`, a UI
  gap). The convert-bug is the single `Notes` drop.
- Rows marked `🔴` have a written test in this suite (skipped until the fix lands).
- When phase 41 completes, regenerate/extend this table from the merged matrix so
  the flow-tier (27–30) and gating (36–40) findings are folded in.
