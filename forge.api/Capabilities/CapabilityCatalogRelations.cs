namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-C — Static encoding of the dependency + mutex edges from the
/// Phase 4A capability catalog markdown. Kept in a separate file from
/// <see cref="CapabilityCatalog"/> so the row list (129 entries) and the
/// relationship list (~100 dependency edges, 1 mutex pair) can evolve
/// independently. Both are consumed by
/// <see cref="CapabilityDependencyResolver"/>.
///
/// Phase C decision: encode edges as static tuples here (now), not as DB rows
/// or per-capability fields on the seeded entity. Reasons:
///   1. Edges are immutable structure, not operator state — they belong in
///      source so a code reviewer scanning the file can see the whole graph.
///   2. The mutation handler can short-circuit on bad relationships at
///      compile-time-equivalent cost.
///   3. Future catalog drift (e.g. an edge whose endpoint is missing from
///      <see cref="CapabilityCatalog"/>) is detected by
///      <see cref="CapabilityDependencyResolver.ValidateGraph(IReadOnlyDictionary{string, CapabilityDefinition})"/>
///      at startup; the seeder logs a warning and skips the bad edge rather
///      than crashing. This keeps the install bootable when 4A drifts.
///
/// Customer/Lead parity audit (post-4F) added 4 dependency edges for the
/// new CAP-MD-CUSTOMER-CONTACTS / -ADDRESSES / -INTERACTIONS and
/// CAP-O2C-CREDIT-LIMITS capabilities, all rooted at CAP-MD-CUSTOMERS.
///
/// "OR" dependencies in 4A (e.g. CAP-O2C-INVOICE depends on
/// CAP-ACCT-BUILTIN OR CAP-ACCT-EXTERNAL) are NOT modelled here — Phase C's
/// gate is "all dependencies must be enabled" (AND semantics). 4D §8.2 names
/// this as the simpler enforcement; OR-dependencies remain a future
/// refinement (preset-apply in Phase G effectively papers over them by
/// always toggling the right peer in lockstep).
/// </summary>
public static class CapabilityCatalogRelations
{
    /// <summary>
    /// Dependency edges: <c>From</c> requires <c>To</c> to also be enabled.
    /// When admin enables <c>From</c>, every <c>To</c> reachable from it
    /// must already be enabled (transitively, via
    /// <see cref="CapabilityDependencyResolver"/>).
    /// </summary>
    public static IReadOnlyList<CapabilityEdge> Dependencies { get; } = new List<CapabilityEdge>
    {
        // ── IDEN ────────────────────────────────────────────────────────────
        new("CAP-IDEN-AUTH-MFA", "CAP-IDEN-AUTH-PASSWORD"),
        new("CAP-IDEN-AUTH-SSO", "CAP-IDEN-AUTH-PASSWORD"),
        new("CAP-IDEN-AUTH-API-KEYS", "CAP-IDEN-AUTH-PASSWORD"),
        new("CAP-IDEN-AUTH-KIOSK", "CAP-IDEN-AUTH-PASSWORD"),
        new("CAP-IDEN-AUTH-KIOSK", "CAP-EXT-SHOPFLOOR-KIOSK"),
        new("CAP-IDEN-USERS", "CAP-IDEN-AUTH-PASSWORD"),
        new("CAP-IDEN-ROLES", "CAP-IDEN-USERS"),
        new("CAP-IDEN-AUDIT-SYSTEM-LOG", "CAP-IDEN-USERS"),

        // ── MD ──────────────────────────────────────────────────────────────
        new("CAP-MD-CUSTOMERS", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-MD-VENDORS", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-MD-PARTS", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-MD-PARTS", "CAP-MD-UOM"),
        new("CAP-MD-BOM", "CAP-MD-PARTS"),
        new("CAP-MD-ROUTING", "CAP-MD-PARTS"),
        new("CAP-MD-ROUTING", "CAP-MD-WORKCENTERS"),
        new("CAP-MD-WORKCENTERS", "CAP-MD-LOCATIONS"),
        new("CAP-MD-LOCATIONS", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-MD-CALENDARS", "CAP-MD-LOCATIONS"),
        new("CAP-MD-EMPLOYEES", "CAP-MD-LOCATIONS"),
        new("CAP-MD-ASSETS", "CAP-MD-LOCATIONS"),
        new("CAP-MD-PRICELIST", "CAP-MD-CUSTOMERS"),
        new("CAP-MD-PRICELIST", "CAP-MD-PARTS"),
        new("CAP-MD-UOM", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-MD-CURRENCIES", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-MD-TAXCODES", "CAP-IDEN-TENANT-CONFIG"),
        // Pro Services rollout (2026-05): removed over-tight edges
        // `CAP-O2C-QUOTE → CAP-MD-PARTS`, `CAP-O2C-SO → CAP-MD-PARTS`, and
        // `CAP-P2P-PO → CAP-MD-PARTS`. Services shops issue quotes, sales
        // orders, and POs whose line items are services (hours / fixed
        // bids / subcontractor work), not parts. The Quote/SO/PO schemas
        // already make PartId optional on line items, and Pro Services
        // code paths populate alternative identifiers (service item codes)
        // rather than parts. These edges were manufacturing-era over-
        // generalizations from the same wave as the kanban/invoice edges.
        new("CAP-MD-CONTRACTS-CONSIGNMENT", "CAP-MD-VENDORS"),
        new("CAP-MD-CONTRACTS-CONSIGNMENT", "CAP-MD-CUSTOMERS"),
        new("CAP-MD-CONTRACTS-CONSIGNMENT", "CAP-MD-PARTS"),
        new("CAP-MD-ECO", "CAP-MD-BOM"),
        new("CAP-MD-CUSTOMER-CONTACTS", "CAP-MD-CUSTOMERS"),
        new("CAP-MD-CUSTOMER-ADDRESSES", "CAP-MD-CUSTOMERS"),
        // Interactions hang off contacts — an interaction has a contact
        // counter-party, so toggling contacts off would orphan the log.
        new("CAP-MD-CUSTOMER-INTERACTIONS", "CAP-MD-CUSTOMERS"),
        new("CAP-MD-CUSTOMER-INTERACTIONS", "CAP-MD-CUSTOMER-CONTACTS"),

        // ── P2P ─────────────────────────────────────────────────────────────
        new("CAP-P2P-PO", "CAP-MD-VENDORS"),
        // CAP-P2P-PO → CAP-MD-PARTS edge removed per Pro Services rollout
        // (see comment above MD block).
        new("CAP-P2P-RFQ", "CAP-P2P-PO"),
        new("CAP-P2P-RFQ", "CAP-MD-VENDORS"),
        new("CAP-P2P-RECEIVE", "CAP-P2P-PO"),
        new("CAP-P2P-AUTOPO", "CAP-P2P-PO"),
        new("CAP-P2P-AUTOPO", "CAP-PLAN-MRP"),
        new("CAP-P2P-DROPSHIP", "CAP-P2P-PO"),
        new("CAP-P2P-DROPSHIP", "CAP-O2C-SO"),
        new("CAP-P2P-BACKTOBACK", "CAP-P2P-PO"),
        new("CAP-P2P-BACKTOBACK", "CAP-O2C-SO"),
        new("CAP-P2P-SUBCONTRACT", "CAP-P2P-PO"),
        new("CAP-P2P-SUBCONTRACT", "CAP-MD-ROUTING"),
        new("CAP-P2P-SUBCONTRACT", "CAP-MFG-WO-RELEASE"),
        new("CAP-P2P-APPROVALS", "CAP-P2P-PO"),
        // AP capability split (owner-ratified 2026-06): bills hang off POs
        // (3-way match references PO + receipt); payments hang off bills
        // (a payment applies against open bills).
        new("CAP-P2P-BILL", "CAP-P2P-PO"),
        new("CAP-P2P-PAY", "CAP-P2P-BILL"),
        // NACHA origination (BANK-002 Phase A) batches vendor payments into ACH files.
        new("CAP-BANK-NACHA", "CAP-P2P-PAY"),

        // ── O2C ─────────────────────────────────────────────────────────────
        new("CAP-O2C-LEAD", "CAP-MD-CUSTOMERS"),
        new("CAP-O2C-CREDIT-LIMITS", "CAP-MD-CUSTOMERS"),
        new("CAP-O2C-QUOTE", "CAP-MD-CUSTOMERS"),
        // CAP-O2C-QUOTE → CAP-MD-PARTS removed per Pro Services rollout.
        new("CAP-O2C-CPQ", "CAP-O2C-QUOTE"),
        new("CAP-O2C-CPQ", "CAP-MD-PRICELIST"),
        new("CAP-O2C-SO", "CAP-MD-CUSTOMERS"),
        // CAP-O2C-SO → CAP-MD-PARTS removed per Pro Services rollout.
        new("CAP-O2C-RECURRING", "CAP-O2C-SO"),
        new("CAP-O2C-PICKPACK", "CAP-O2C-SO"),
        new("CAP-O2C-PICKPACK", "CAP-INV-CORE"),
        new("CAP-O2C-SHIP", "CAP-O2C-PICKPACK"),
        // Note: CAP-O2C-INVOICE depends on (CAP-ACCT-BUILTIN OR CAP-ACCT-EXTERNAL) per 4A —
        // Phase C uses AND semantics, so we model the operationally-dominant edge.
        //
        // Pro Services rollout (2026-05): removed the
        // `CAP-O2C-INVOICE → CAP-O2C-SHIP` edge. Services shops invoice
        // billable hours / fixed bids / retainer renewals without a
        // shipment — the edge was a manufacturing-era over-generalization.
        // Invoicing in the codebase doesn't actually call into shipment
        // code; the dependency was structural rather than functional.
        new("CAP-O2C-INVOICE", "CAP-MD-TAXCODES"),
        new("CAP-O2C-CASH", "CAP-O2C-INVOICE"),
        new("CAP-O2C-COLLECTIONS", "CAP-O2C-CASH"),
        new("CAP-O2C-CREDITMEMO", "CAP-O2C-INVOICE"),
        new("CAP-O2C-RMA", "CAP-O2C-INVOICE"),
        new("CAP-O2C-RMA", "CAP-INV-CORE"),

        // ── MFG ─────────────────────────────────────────────────────────────
        new("CAP-MFG-WO-RELEASE", "CAP-MD-BOM"),
        new("CAP-MFG-WO-RELEASE", "CAP-MD-ROUTING"),
        new("CAP-MFG-MATL-ISSUE", "CAP-MFG-WO-RELEASE"),
        new("CAP-MFG-MATL-ISSUE", "CAP-INV-CORE"),
        new("CAP-MFG-BACKFLUSH", "CAP-MFG-WO-RELEASE"),
        new("CAP-MFG-BACKFLUSH", "CAP-INV-CORE"),
        new("CAP-MFG-LABOR", "CAP-MFG-WO-RELEASE"),
        new("CAP-MFG-LABOR", "CAP-MD-EMPLOYEES"),
        new("CAP-MFG-MULTIOP", "CAP-MD-ROUTING"),
        new("CAP-MFG-MULTIOP", "CAP-MFG-WO-RELEASE"),
        new("CAP-MFG-COMPLETE", "CAP-MFG-WO-RELEASE"),
        new("CAP-MFG-WOVARIANCE", "CAP-MFG-COMPLETE"),
        new("CAP-MFG-WOVARIANCE", "CAP-MFG-LABOR"),
        new("CAP-MFG-WOVARIANCE", "CAP-MFG-MATL-ISSUE"),
        new("CAP-MFG-SHOPFLOOR", "CAP-MFG-WO-RELEASE"),
        new("CAP-MFG-SHOPFLOOR", "CAP-MFG-LABOR"),
        new("CAP-MFG-STOPPAGE", "CAP-MFG-SHOPFLOOR"),
        new("CAP-MFG-MACHINE-CONNECT", "CAP-MD-WORKCENTERS"),

        // ── PLAN ────────────────────────────────────────────────────────────
        new("CAP-PLAN-MRP", "CAP-MD-PARTS"),
        new("CAP-PLAN-MRP", "CAP-MD-BOM"),
        new("CAP-PLAN-MRP", "CAP-INV-CORE"),
        new("CAP-PLAN-MPS", "CAP-PLAN-MRP"),
        new("CAP-PLAN-MPS", "CAP-PLAN-FORECAST"),
        new("CAP-PLAN-FORECAST", "CAP-MD-PARTS"),
        new("CAP-PLAN-FORECAST", "CAP-MD-CUSTOMERS"),
        new("CAP-PLAN-CAPACITY", "CAP-MD-WORKCENTERS"),
        new("CAP-PLAN-CAPACITY", "CAP-MD-CALENDARS"),
        new("CAP-PLAN-SAFETYSTOCK", "CAP-MD-PARTS"),
        new("CAP-PLAN-SAFETYSTOCK", "CAP-INV-CORE"),
        new("CAP-PLAN-ATP", "CAP-INV-CORE"),
        new("CAP-PLAN-ATP", "CAP-O2C-QUOTE"),
        new("CAP-PLAN-ABC", "CAP-MD-PARTS"),
        new("CAP-PLAN-ABC", "CAP-INV-CORE"),

        // ── INV ─────────────────────────────────────────────────────────────
        new("CAP-INV-CORE", "CAP-MD-PARTS"),
        new("CAP-INV-CORE", "CAP-MD-LOCATIONS"),
        new("CAP-INV-MULTILOC", "CAP-INV-CORE"),
        new("CAP-INV-MULTILOC", "CAP-MD-LOCATIONS"),
        new("CAP-INV-CYCLECOUNT", "CAP-INV-CORE"),
        new("CAP-INV-PHYSICAL", "CAP-INV-CORE"),
        new("CAP-INV-LOTS", "CAP-INV-CORE"),
        new("CAP-INV-LOTS", "CAP-MD-PARTS"),
        new("CAP-INV-SERIALS", "CAP-INV-CORE"),
        new("CAP-INV-SERIALS", "CAP-MD-PARTS"),
        new("CAP-INV-RESERVE", "CAP-INV-CORE"),
        new("CAP-INV-RESERVE", "CAP-O2C-SO"),
        new("CAP-INV-PICKWAVE", "CAP-INV-CORE"),
        new("CAP-INV-PICKWAVE", "CAP-O2C-PICKPACK"),
        new("CAP-INV-HAZMAT", "CAP-INV-CORE"),
        new("CAP-INV-HAZMAT", "CAP-MD-PARTS"),

        // ── QC ──────────────────────────────────────────────────────────────
        new("CAP-QC-INSPECTION", "CAP-MD-PARTS"),
        new("CAP-QC-NCR", "CAP-QC-INSPECTION"),
        new("CAP-QC-CAPA", "CAP-QC-NCR"),
        new("CAP-QC-FMEA", "CAP-MD-PARTS"),
        new("CAP-QC-FMEA", "CAP-MD-ROUTING"),
        new("CAP-QC-PPAP", "CAP-MD-PARTS"),
        new("CAP-QC-SPC", "CAP-QC-INSPECTION"),
        new("CAP-QC-GAGE", "CAP-MD-ASSETS"),
        // CAP-QC-RECALL: 4A says "CAP-INV-LOTS OR CAP-INV-SERIALS" — Phase C
        // models the lot edge (the dominant case in food/pharma). Either-or
        // semantics revisited at preset time.
        new("CAP-QC-RECALL", "CAP-INV-LOTS"),
        new("CAP-QC-COA", "CAP-INV-LOTS"),
        new("CAP-QC-COA", "CAP-O2C-SHIP"),
        new("CAP-QC-COMPLIANCE-FORMS", "CAP-MD-EMPLOYEES"),

        // ── MAINT ───────────────────────────────────────────────────────────
        new("CAP-MAINT-PM", "CAP-MD-ASSETS"),
        new("CAP-MAINT-BREAKDOWN", "CAP-MD-ASSETS"),
        new("CAP-MAINT-PREDICTIVE", "CAP-MD-ASSETS"),
        new("CAP-MAINT-PREDICTIVE", "CAP-MFG-MACHINE-CONNECT"),
        new("CAP-MAINT-ASSETLIFECYCLE", "CAP-MD-ASSETS"),

        // ── ACCT ────────────────────────────────────────────────────────────
        new("CAP-ACCT-EXTERNAL", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-ACCT-BUILTIN", "CAP-IDEN-TENANT-CONFIG"),
        new("CAP-ACCT-FULLGL", "CAP-ACCT-BUILTIN"),
        new("CAP-ACCT-EXPENSES", "CAP-MD-EMPLOYEES"),
        new("CAP-ACCT-PERIOD", "CAP-ACCT-FULLGL"),
        new("CAP-ACCT-DEPRECIATION", "CAP-MD-ASSETS"),
        new("CAP-ACCT-DEPRECIATION", "CAP-ACCT-FULLGL"),
        new("CAP-ACCT-FXREVAL", "CAP-MD-CURRENCIES"),
        new("CAP-ACCT-FXREVAL", "CAP-ACCT-FULLGL"),
        // QB-001: the QBO push exports FROM the built-in GL — it depends on FULLGL and is
        // deliberately NOT in the BUILTIN ⊥ EXTERNAL mutex (downstream export, not external mode).
        new("CAP-ACCT-QBO-EXPORT", "CAP-ACCT-FULLGL"),

        // ── HR ──────────────────────────────────────────────────────────────
        new("CAP-HR-HIRE", "CAP-MD-EMPLOYEES"),
        new("CAP-HR-HIRE", "CAP-IDEN-USERS"),
        new("CAP-HR-TERMINATION", "CAP-MD-EMPLOYEES"),
        new("CAP-HR-LEAVE", "CAP-MD-EMPLOYEES"),
        new("CAP-HR-TIMETRACK", "CAP-MD-EMPLOYEES"),
        new("CAP-HR-SHIFTS", "CAP-MD-EMPLOYEES"),
        new("CAP-HR-SHIFTS", "CAP-MD-CALENDARS"),
        new("CAP-HR-PAYROLL", "CAP-HR-TIMETRACK"),
        new("CAP-HR-TRAINING", "CAP-MD-EMPLOYEES"),
        new("CAP-HR-REVIEW", "CAP-MD-EMPLOYEES"),

        // ── RPT ─────────────────────────────────────────────────────────────
        new("CAP-RPT-OPERATIONAL", "CAP-MD-PARTS"),
        new("CAP-RPT-OPERATIONAL", "CAP-MD-CUSTOMERS"),
        new("CAP-RPT-OPERATIONAL", "CAP-MD-VENDORS"),
        new("CAP-RPT-INVVAL", "CAP-INV-CORE"),
        new("CAP-RPT-MRPEX", "CAP-PLAN-MRP"),
        // CAP-RPT-DASHBOARDS → CAP-RPT-OPERATIONAL edge removed per Pro
        // Services rollout. Dashboards are a generic UI surface for widgets;
        // they should work with whatever per-preset widget set is loaded,
        // not be hard-tied to operational reports. PRESET-08 ships service-
        // flavored dashboard widgets (utilization, billable %, retainer
        // burn-down) that don't depend on manufacturing operational reports.

        // ── CROSS ───────────────────────────────────────────────────────────
        new("CAP-CROSS-PERMS-MATRIX", "CAP-IDEN-ROLES"),
        new("CAP-CROSS-ACTIVITY-LOG", "CAP-IDEN-USERS"),
        new("CAP-CROSS-LIST-UX", "CAP-IDEN-USERS"),
        new("CAP-CROSS-BULK-OPS", "CAP-CROSS-LIST-UX"),
        new("CAP-CROSS-NOTIFICATIONS", "CAP-IDEN-USERS"),
        new("CAP-CROSS-INTEG-EDI", "CAP-MD-VENDORS"),
        new("CAP-CROSS-INTEG-EDI", "CAP-MD-CUSTOMERS"),
        new("CAP-CROSS-BI-EXPORT", "CAP-IDEN-AUTH-API-KEYS"),

        // ── EXT ─────────────────────────────────────────────────────────────
        // Pro Services rollout (2026-05): removed the
        // `CAP-EXT-KANBAN → CAP-MFG-WO-RELEASE` edge. Kanban is a UI surface
        // that renders Jobs grouped by stage — no shop-floor / WO-release
        // code is invoked. Pro Services engagements ARE Jobs (per G-17
        // spike) on the Engagement track type, so kanban needs to work
        // without the manufacturing capability.
        new("CAP-EXT-KANBAN-REPLENISHMENT", "CAP-EXT-KANBAN"),
        new("CAP-EXT-KANBAN-REPLENISHMENT", "CAP-INV-CORE"),
        new("CAP-EXT-MOBILE", "CAP-IDEN-USERS"),
        new("CAP-EXT-SHOPFLOOR-KIOSK", "CAP-MFG-SHOPFLOOR"),
        new("CAP-EXT-CHAT", "CAP-IDEN-USERS"),
        new("CAP-EXT-CHAT-INTEGRATION", "CAP-CROSS-NOTIFICATIONS"),
        new("CAP-EXT-AI-ASSISTANT", "CAP-IDEN-USERS"),
        new("CAP-EXT-AI-ASSISTANT", "CAP-CROSS-ATTACHMENTS"),
        new("CAP-EXT-ANDON", "CAP-MFG-SHOPFLOOR"),
        new("CAP-EXT-PROJECTS", "CAP-IDEN-USERS"),
        new("CAP-EXT-ANNOUNCEMENTS", "CAP-IDEN-USERS"),
        // Wave 8 — communication sync. Both channels need user accounts
        // (per-user OAuth / token storage) and the customer master that
        // anchors the matched ContactInteraction rows.
        new("CAP-EXT-EMAIL-SYNC", "CAP-IDEN-USERS"),
        new("CAP-EXT-EMAIL-SYNC", "CAP-MD-CUSTOMERS"),
        new("CAP-EXT-VOIP-SYNC", "CAP-IDEN-USERS"),
        new("CAP-EXT-VOIP-SYNC", "CAP-MD-CUSTOMERS"),

        // ── Pro Services rollout (Phase 2) — cloud storage ──
        // Each provider depends on the umbrella; providers are NOT mutually
        // exclusive (hybrid storage explicitly supported per D9).
        new("CAP-EXT-CLOUD-STORAGE-GDRIVE", "CAP-EXT-CLOUD-STORAGE"),
        new("CAP-EXT-CLOUD-STORAGE-ONEDRIVE", "CAP-EXT-CLOUD-STORAGE"),
        new("CAP-EXT-CLOUD-STORAGE-DROPBOX", "CAP-EXT-CLOUD-STORAGE"),

        // ── Pro Services rollout (Phase 3a) — PS area ──
        // CAP-PS-ENGAGEMENT has no hard dependency on identity / MD layers —
        // it operates on Job which is already gated by CAP-EXT-KANBAN.
        // Sub-capabilities depend on CAP-PS-ENGAGEMENT (the engagement
        // primitive) and on whatever functional pre-reqs they layer on.
        new("CAP-PS-RETAINER", "CAP-O2C-RECURRING"),
        new("CAP-PS-RETAINER", "CAP-PS-ENGAGEMENT"),
        new("CAP-PS-PROJECT-COST", "CAP-PS-ENGAGEMENT"),
        new("CAP-PS-UTILIZATION", "CAP-PS-ENGAGEMENT"),
        new("CAP-PS-UTILIZATION", "CAP-PS-TIME-BILLABLE"),
    };

    /// <summary>
    /// Soft-mutex pairs: when both endpoints would be enabled simultaneously,
    /// the second toggle is rejected with 409 + a message instructing the
    /// admin to disable the peer first. Unordered (mutex is symmetric).
    /// 4A documents one explicit pair (CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN).
    /// </summary>
    public static IReadOnlyList<CapabilityEdge> Mutexes { get; } = new List<CapabilityEdge>
    {
        new("CAP-ACCT-EXTERNAL", "CAP-ACCT-BUILTIN"),
    };
}
