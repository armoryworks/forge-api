# Standard costing + 6-way variance decomposition — build plan

**Owner decision (2026-06-07):** move inventory to **standard-cost carrying** and decompose production
variance into the **full 6 standard-cost variance slots**, with a real **overhead cost pool** (so the overhead
spending variance isn't a placeholder). Lumped variance is then simply `SUM(variance accounts)` at report time
— there is no "lumped vs split" posting mode; one set of postings yields both.

All work is **dark** behind `CAP-ACCT-FULLGL`, branch-only (`feat/accounting-gl-phase1`), additive migrations
NOT applied, nothing deployed.

## The 6 variance slots (and where each is recognized)

| Element | Variance | Recognized at | Formula | Data source |
|---|---|---|---|---|
| Material | **Price** (PPV) | PO **receipt** | `(std − landed) × qty` | part std cost vs PO price+freight |
| Material | **Usage** | material **issue** / job close | `(actual qty − std qty) × std cost` | issues vs BOM std qty × output |
| Labor | **Rate** | time entry | `(std rate − actual rate) × actual hrs` | actual pay rate vs `LaborRate.StandardRatePerHour` |
| Labor | **Efficiency** | time entry / job close | `(std hrs − actual hrs) × std rate` | routing `SetupMinutes`/`RunMinutesEach` vs actual `TimeEntry` hrs |
| Overhead | **Spending** | period / job close | `applied − actual pool` | OH cost pool actual vs applied |
| Overhead | **Efficiency** (volume) | time entry / job close | `(std hrs − actual hrs) × OH rate` | routing hrs vs actual hrs |

Sign convention: **debit = unfavorable** (actual exceeded standard), **credit = favorable**. Standard-cost
inventory is carried at standard; every actual-vs-standard difference lands in one of these accounts as it
arises, so WIP/inventory stays at standard by construction and the job-close "production variance" sweep
becomes a *reconciliation* (ideally ~0) rather than the primary recognition point.

## Chart of accounts (this commit)

Variance (expense, debit-normal): `MATERIAL_PRICE_VARIANCE` (51000 = existing PPV, reused), new
`MATERIAL_USAGE_VARIANCE` (51100, existing), `LABOR_RATE_VARIANCE` (51300), `LABOR_EFFICIENCY_VARIANCE`
(51310), `OVERHEAD_SPENDING_VARIANCE` (51320), `OVERHEAD_EFFICIENCY_VARIANCE` (51330). `PRODUCTION_VARIANCE`
(51200) stays as the job-close reconciliation catch-all.
Absorption clearing (contra-expense, credit-normal): `LABOR_APPLIED` (51210), `OVERHEAD_APPLIED` (51220) —
already seeded. Overhead pool (asset/clearing): `OVERHEAD_CONTROL` (13400) accumulates actual overhead and is
relieved as applied.

## Phases

0. **StandardCostResolver** — `(material, labor, overhead)` std unit cost for a part. Priority:
   `CurrentCostCalculation.Inputs` (DirectMaterialCost / DirectLaborCost / OverheadAmount) → routing+BOM rollup
   (Σ BOM material, Σ operations labor+OH) → `ManualCostOverride` as material-only (blended fallback). Backbone
   for every variance.
1. **Receipt at standard + material price variance.** `ReceiptInventoryPostingService`: Dr INVENTORY at
   **standard** / Cr GRNI (PO base) / Cr FREIGHT_CLEARING / Dr|Cr `MATERIAL_PRICE_VARIANCE` (plug = std −
   landed). Valuation store carries standard. *(Converts the current actual/landed receipt.)*
2. **Issue at standard + material usage variance.** Relieve INVENTORY_RAW at std × actual qty; charge WIP at
   std × **standard** qty (BOM requirement for output); Dr|Cr `MATERIAL_USAGE_VARIANCE` for the qty diff.
3. **Labor rate + efficiency.** Capture actual labor rate; apply labor to WIP at std hrs × std rate; recognize
   `LABOR_RATE_VARIANCE` + `LABOR_EFFICIENCY_VARIANCE`.
4. **Overhead cost pool + spending + efficiency.** New pool model (pool + driver + rate); actual overhead
   accumulates in `OVERHEAD_CONTROL`; applied to WIP at std rate × driver; `OVERHEAD_SPENDING_VARIANCE`
   (actual pool vs applied) + `OVERHEAD_EFFICIENCY_VARIANCE`.
5. **COGS at standard** + rework job-close as the reconciliation; **lumped variance = `SUM(variance accounts)`**
   reporting endpoint.

Each phase: build → migration (if schema) → tests → full suite green → commit. Existing actual-cost receipt/
issue/COGS tests get rewritten to the standard-cost postings as their phase lands.
