using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Core.Enums;

namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — Authoritative static catalog of the 8 presets from
/// Phase 4B preset-design.md. The recommendation engine routes to one of
/// the 7 named presets; PRESET-CUSTOM is the explicit opt-out.
///
/// Each preset's <see cref="PresetDefinition.EnabledCapabilities"/> is
/// computed from the 4B per-preset spec by:
///   1. Starting from the catalog's 41 default-on entries
///   2. Removing entries the preset explicitly disables (e.g. PRESET-03
///      Distribution disables BOM/ROUTING/MFG-*).
///   3. Adding entries the preset explicitly enables (per 4B "explicitly
///      enabled" markers).
///
/// PRESET-CUSTOM is empty — apply-time logic substitutes the 41 catalog
/// defaults (per 4B Open Question 5 / 4F-decisions-log).
/// </summary>
public static class PresetCatalog
{
    /// <summary>The 41 default-on capabilities from the 4A catalog. Used as a baseline for preset assembly.</summary>
    private static readonly string[] CatalogDefaultsBaseline =
    [
        "CAP-IDEN-AUTH-PASSWORD", "CAP-IDEN-USERS", "CAP-IDEN-ROLES",
        "CAP-IDEN-TENANT-CONFIG", "CAP-IDEN-AUDIT-SYSTEM-LOG",
        "CAP-MD-CUSTOMERS", "CAP-MD-VENDORS", "CAP-MD-PARTS", "CAP-MD-BOM",
        "CAP-MD-ROUTING", "CAP-MD-WORKCENTERS", "CAP-MD-LOCATIONS",
        "CAP-MD-CALENDARS", "CAP-MD-EMPLOYEES", "CAP-MD-ASSETS", "CAP-MD-UOM",
        "CAP-MD-CURRENCIES", "CAP-MD-TAXCODES",
        "CAP-P2P-PO", "CAP-P2P-RECEIVE",
        "CAP-O2C-QUOTE", "CAP-O2C-SO", "CAP-O2C-PICKPACK", "CAP-O2C-SHIP",
        "CAP-O2C-INVOICE", "CAP-O2C-CASH",
        "CAP-MFG-WO-RELEASE", "CAP-MFG-MATL-ISSUE", "CAP-MFG-LABOR",
        "CAP-MFG-MULTIOP", "CAP-MFG-COMPLETE", "CAP-MFG-SHOPFLOOR",
        "CAP-INV-CORE", "CAP-INV-CYCLECOUNT",
        "CAP-MAINT-ASSETLIFECYCLE",
        "CAP-ACCT-BUILTIN", "CAP-ACCT-EXPENSES",
        "CAP-HR-HIRE", "CAP-HR-TERMINATION", "CAP-HR-TIMETRACK",
        "CAP-RPT-OPERATIONAL", "CAP-RPT-INVVAL", "CAP-RPT-DASHBOARDS",
        "CAP-CROSS-PERMS-MATRIX", "CAP-CROSS-ACTIVITY-LOG", "CAP-CROSS-LIST-UX",
        "CAP-CROSS-BULK-OPS", "CAP-CROSS-DOCS", "CAP-CROSS-ATTACHMENTS",
        "CAP-CROSS-NOTIFICATIONS", "CAP-CROSS-INTEG-FILE", "CAP-CROSS-CONCURRENCY",
        "CAP-EXT-KANBAN", "CAP-EXT-MOBILE",
        "CAP-IDEN-CAPABILITY-ADMIN",
    ];

    private static IReadOnlyList<string> AssemblePreset(
        IEnumerable<string> remove,
        IEnumerable<string> add)
    {
        var set = new HashSet<string>(CatalogDefaultsBaseline, StringComparer.Ordinal);
        foreach (var r in remove) set.Remove(r);
        foreach (var a in add) set.Add(a);
        return set.OrderBy(c => c, StringComparer.Ordinal).ToList();
    }

    public static PresetDefinition Preset01_TwoPersonShop { get; } = new(
        Id: "PRESET-01",
        Name: "Two-Person Shop",
        ShortDescription: "An owner-operator or two-person business doing light manufacturing or trade work, with built-in lightweight accounting and minimum ceremony.",
        TargetProfile: "1–3 people, single product line or low-volume job shop, no inspection requirements, single location.",
        EnabledCapabilities: AssemblePreset(
            // Disabled vs catalog defaults (per 4B PRESET-01)
            remove: [],
            // Explicitly enabled
            add: []));

    public static PresetDefinition Preset02_GrowingJobShop { get; } = new(
        Id: "PRESET-02",
        Name: "Growing Job Shop",
        ShortDescription: "Small-to-mid manufacturer with shop-floor / office split, external accounting (QuickBooks), light planning, and shop-floor kiosk auth.",
        TargetProfile: "4–25 people, 2–6 work centers, mixed repeat and one-off jobs, no regulated industry.",
        EnabledCapabilities: AssemblePreset(
            remove: ["CAP-ACCT-BUILTIN"],
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST",
                "CAP-P2P-RFQ", "CAP-P2P-SUBCONTRACT",
                "CAP-O2C-RMA", "CAP-O2C-COLLECTIONS",
                "CAP-PLAN-SAFETYSTOCK",
                "CAP-INV-RESERVE",
                "CAP-IDEN-AUTH-KIOSK", "CAP-EXT-SHOPFLOOR-KIOSK",
                "CAP-HR-LEAVE",
            ]));

    public static PresetDefinition Preset03_Distribution { get; } = new(
        Id: "PRESET-03",
        Name: "Distribution / Wholesale",
        ShortDescription: "Pure-resell or assembly-light business — buying, storing, picking, shipping. Heavy inventory and procurement automation, no production.",
        TargetProfile: "5–50 people, 50–500+ SKUs, drop-ship and back-to-back patterns, often industrial supply / food / beverage.",
        EnabledCapabilities: AssemblePreset(
            remove:
            [
                "CAP-MD-BOM", "CAP-MD-ROUTING", "CAP-MD-WORKCENTERS", "CAP-MD-CALENDARS",
                "CAP-MFG-WO-RELEASE", "CAP-MFG-MATL-ISSUE", "CAP-MFG-LABOR",
                "CAP-MFG-MULTIOP", "CAP-MFG-COMPLETE", "CAP-MFG-SHOPFLOOR",
                "CAP-ACCT-BUILTIN",
                "CAP-EXT-KANBAN",
            ],
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST",
                "CAP-P2P-RFQ", "CAP-P2P-DROPSHIP", "CAP-P2P-BACKTOBACK",
                "CAP-O2C-LEAD", "CAP-O2C-COLLECTIONS", "CAP-O2C-RMA", "CAP-O2C-CREDITMEMO",
                "CAP-PLAN-SAFETYSTOCK", "CAP-PLAN-FORECAST",
                "CAP-INV-RESERVE", "CAP-INV-PICKWAVE",
                "CAP-HR-LEAVE", "CAP-HR-SHIFTS",
            ]));

    public static PresetDefinition Preset04_ProductionManufacturer { get; } = new(
        Id: "PRESET-04",
        Name: "Production Manufacturer",
        ShortDescription: "A real manufacturer with dedicated production manager, formal routings, multi-step shop-floor, variance review, and ISO 9001 baseline quality.",
        TargetProfile: "25–200 people, 5–50 work centers, dedicated production / buyer / quality roles.",
        EnabledCapabilities: AssemblePreset(
            remove: ["CAP-ACCT-BUILTIN"],
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST", "CAP-MD-ECO",
                "CAP-P2P-RFQ", "CAP-P2P-SUBCONTRACT", "CAP-P2P-APPROVALS",
                "CAP-O2C-COLLECTIONS", "CAP-O2C-RMA", "CAP-O2C-CREDITMEMO",
                "CAP-MFG-WOVARIANCE", "CAP-MFG-STOPPAGE",
                "CAP-PLAN-SAFETYSTOCK", "CAP-PLAN-CAPACITY",
                "CAP-INV-RESERVE",
                "CAP-QC-INSPECTION", "CAP-QC-NCR", "CAP-QC-CAPA",
                "CAP-MAINT-PM", "CAP-MAINT-BREAKDOWN",
                "CAP-IDEN-AUTH-KIOSK", "CAP-EXT-SHOPFLOOR-KIOSK", "CAP-EXT-ANDON",
                "CAP-HR-LEAVE", "CAP-HR-SHIFTS", "CAP-HR-TRAINING",
                "CAP-RPT-OEE",
            ]),
        // ── Track type bundle (G-03/04 per Artifact 3) ──
        // Mirrors the manufacturing track types currently seeded by
        // SeedData.Essential.cs (Production / R&D / Maintenance). Bundle
        // exists so that re-applying PRESET-04 reasserts the canonical
        // manufacturing kanban shape; on a fresh install the existing
        // hardcoded seeder runs first, then this bundle's UpsertByCode
        // semantics are a no-op (codes already exist, stages already
        // present). Refactoring the seeder to run via this bundle alone
        // is a Phase 3a follow-up — for now this is additive.
        TrackTypeBundle: new TrackTypeBundle(
            TrackTypes: new List<TrackTypeSeed>
            {
                new(
                    Code: "production",
                    Name: "Production",
                    SortOrder: 1,
                    IsDefault: true,
                    IsShopFloor: true,
                    Stages: new List<JobStageSeed>
                    {
                        new("quote_requested",   "Quote Requested",   1, "#94a3b8"),
                        new("quoted",            "Quoted",            2, "#0d9488", AccountingDocumentType: AccountingDocumentType.Estimate),
                        new("order_confirmed",   "Order Confirmed",   3, "#0ea5e9", AccountingDocumentType: AccountingDocumentType.SalesOrder),
                        new("materials_ordered", "Materials Ordered", 4, "#8b5cf6", IsShopFloor: true, AccountingDocumentType: AccountingDocumentType.PurchaseOrder),
                        new("materials_received","Materials Received",5, "#a855f7", IsShopFloor: true),
                        new("in_production",     "In Production",     6, "#f59e0b", IsShopFloor: true),
                        new("qc_review",         "QC/Review",         7, "#ec4899", IsShopFloor: true),
                        new("shipped",           "Shipped",           8, "#c2410c", IsShopFloor: true, AccountingDocumentType: AccountingDocumentType.Invoice),
                        new("invoiced_sent",     "Invoiced/Sent",     9, "#dc2626", AccountingDocumentType: AccountingDocumentType.Invoice, IsIrreversible: true),
                        new("payment_received",  "Payment Received", 10, "#15803d", AccountingDocumentType: AccountingDocumentType.Payment, IsIrreversible: true),
                    }),
                new(
                    Code: "rnd",
                    Name: "R&D/Tooling",
                    SortOrder: 2,
                    IsDefault: false,
                    IsShopFloor: false,
                    Stages: new List<JobStageSeed>
                    {
                        new("concept",          "Concept",          1, "#94a3b8"),
                        new("design",           "Design",           2, "#0d9488"),
                        new("prototype",        "Prototype",        3, "#0ea5e9", IsShopFloor: true),
                        new("test",             "Test",             4, "#f59e0b", IsShopFloor: true),
                        new("iterate",          "Iterate",          5, "#ec4899", IsShopFloor: true),
                        new("production_ready", "Production Ready", 6, "#15803d"),
                    }),
                new(
                    Code: "maintenance",
                    Name: "Maintenance",
                    SortOrder: 3,
                    IsDefault: false,
                    IsShopFloor: true,
                    Stages: new List<JobStageSeed>
                    {
                        new("requested",   "Requested",   1, "#94a3b8"),
                        new("scheduled",   "Scheduled",   2, "#0ea5e9", IsShopFloor: true),
                        new("in_progress", "In Progress", 3, "#f59e0b", IsShopFloor: true),
                        new("complete",    "Complete",    4, "#15803d", IsShopFloor: true),
                    }),
            }));

    public static PresetDefinition Preset05_RegulatedManufacturer { get; } = new(
        Id: "PRESET-05",
        Name: "Regulated Manufacturer",
        ShortDescription: "Manufacturer subject to industry compliance (ISO 13485, AS9100, IATF 16949, FDA, FSMA). Full QC stack — lots, ECO, gage, recall, training.",
        TargetProfile: "10–500 people (regulation orthogonal to size); industry cert; lot or serial traceability mandated; formal CAPA system.",
        EnabledCapabilities: AssemblePreset(
            remove: ["CAP-ACCT-BUILTIN"],
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST", "CAP-MD-ECO",
                "CAP-P2P-RFQ", "CAP-P2P-SUBCONTRACT", "CAP-P2P-APPROVALS",
                "CAP-O2C-COLLECTIONS", "CAP-O2C-RMA", "CAP-O2C-CREDITMEMO",
                "CAP-MFG-WOVARIANCE", "CAP-MFG-STOPPAGE",
                "CAP-PLAN-SAFETYSTOCK", "CAP-PLAN-CAPACITY",
                "CAP-INV-RESERVE", "CAP-INV-LOTS",
                "CAP-QC-INSPECTION", "CAP-QC-NCR", "CAP-QC-CAPA",
                "CAP-QC-GAGE", "CAP-QC-RECALL", "CAP-QC-COA",
                "CAP-QC-COMPLIANCE-FORMS",
                "CAP-MAINT-PM", "CAP-MAINT-BREAKDOWN",
                "CAP-IDEN-AUTH-KIOSK", "CAP-EXT-SHOPFLOOR-KIOSK", "CAP-EXT-ANDON",
                "CAP-EXT-ANNOUNCEMENTS",
                "CAP-HR-LEAVE", "CAP-HR-SHIFTS", "CAP-HR-TRAINING",
                "CAP-RPT-OEE",
            ]));

    public static PresetDefinition Preset06_MultiSite { get; } = new(
        Id: "PRESET-06",
        Name: "Multi-Site Operation",
        ShortDescription: "Manufacturer or distributor operating from 2+ plants with inter-site transfers, MRP/MPS, EDI, and corporate-level reporting.",
        TargetProfile: "50–500 people across sites; 2+ plants/warehouses; weekly+ inter-site transfers; corporate consolidation.",
        EnabledCapabilities: AssemblePreset(
            remove: ["CAP-ACCT-BUILTIN"],
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST", "CAP-MD-ECO", "CAP-MD-CONTRACTS-CONSIGNMENT",
                "CAP-P2P-RFQ", "CAP-P2P-SUBCONTRACT", "CAP-P2P-APPROVALS",
                "CAP-O2C-LEAD", "CAP-O2C-COLLECTIONS", "CAP-O2C-RMA", "CAP-O2C-CREDITMEMO",
                "CAP-MFG-WOVARIANCE", "CAP-MFG-STOPPAGE",
                "CAP-PLAN-SAFETYSTOCK", "CAP-PLAN-CAPACITY",
                "CAP-PLAN-MRP", "CAP-PLAN-MPS",
                "CAP-INV-RESERVE", "CAP-INV-MULTILOC",
                "CAP-QC-INSPECTION", "CAP-QC-NCR", "CAP-QC-CAPA",
                "CAP-MAINT-PM", "CAP-MAINT-BREAKDOWN",
                "CAP-IDEN-AUTH-KIOSK", "CAP-EXT-SHOPFLOOR-KIOSK", "CAP-EXT-ANDON",
                "CAP-EXT-ANNOUNCEMENTS",
                "CAP-CROSS-INTEG-EDI",
                "CAP-HR-LEAVE", "CAP-HR-SHIFTS", "CAP-HR-TRAINING",
                "CAP-RPT-OEE", "CAP-RPT-MRPEX",
            ]));

    public static PresetDefinition Preset07_Enterprise { get; } = new(
        Id: "PRESET-07",
        Name: "Enterprise",
        ShortDescription: "Large complex manufacturer / hybrid distributor with multi-currency, EDI, CPQ, APS, machine connect, BI export, SSO/MFA.",
        TargetProfile: "200+ people, multi-site, multi-currency, configure-to-order or engineer-to-order, formal supply-chain integration.",
        EnabledCapabilities: AssemblePreset(
            remove: ["CAP-ACCT-BUILTIN"],
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST", "CAP-MD-ECO", "CAP-MD-CONTRACTS-CONSIGNMENT",
                "CAP-P2P-RFQ", "CAP-P2P-AUTOPO", "CAP-P2P-DROPSHIP", "CAP-P2P-BACKTOBACK",
                "CAP-P2P-SUBCONTRACT", "CAP-P2P-APPROVALS",
                "CAP-O2C-LEAD", "CAP-O2C-CPQ", "CAP-O2C-RECURRING",
                "CAP-O2C-COLLECTIONS", "CAP-O2C-RMA", "CAP-O2C-CREDITMEMO",
                "CAP-MFG-BACKFLUSH", "CAP-MFG-WOVARIANCE", "CAP-MFG-STOPPAGE",
                "CAP-MFG-MACHINE-CONNECT",
                "CAP-PLAN-SAFETYSTOCK", "CAP-PLAN-CAPACITY",
                "CAP-PLAN-MRP", "CAP-PLAN-MPS", "CAP-PLAN-FORECAST",
                "CAP-PLAN-ATP", "CAP-PLAN-ABC",
                "CAP-INV-RESERVE", "CAP-INV-MULTILOC", "CAP-INV-PICKWAVE",
                "CAP-INV-PHYSICAL", "CAP-INV-LOTS",
                "CAP-QC-INSPECTION", "CAP-QC-NCR", "CAP-QC-CAPA",
                "CAP-QC-GAGE", "CAP-QC-RECALL",
                "CAP-MAINT-PM", "CAP-MAINT-BREAKDOWN", "CAP-MAINT-PREDICTIVE",
                "CAP-IDEN-AUTH-MFA", "CAP-IDEN-AUTH-SSO", "CAP-IDEN-AUTH-API-KEYS",
                "CAP-IDEN-AUTH-KIOSK",
                "CAP-EXT-SHOPFLOOR-KIOSK", "CAP-EXT-ANDON", "CAP-EXT-ANNOUNCEMENTS",
                "CAP-EXT-CHAT", "CAP-EXT-PROJECTS",
                "CAP-CROSS-INTEG-EDI", "CAP-CROSS-WEBHOOKS", "CAP-CROSS-BI-EXPORT",
                "CAP-HR-LEAVE", "CAP-HR-SHIFTS", "CAP-HR-PAYROLL",
                "CAP-HR-TRAINING", "CAP-HR-REVIEW",
                "CAP-RPT-OEE", "CAP-RPT-MRPEX",
            ]));

    // ─── Pro Services rollout (Phase 2 foundations) ──────────────────────────
    // PRESET-08 (Pro Services) and PRESET-09 (Hybrid) carry full bundles.
    // Terminology (Job → Project, Customer → Client, Work Center → Consultant)
    // per user direction. Apply pipeline (ApplyPresetHandler) reads non-null
    // bundles and seeds each layer per its conflict policy.

    public static PresetDefinition Preset08_ProServices { get; } = new(
        Id: "PRESET-08",
        Name: "Pro Services",
        ShortDescription: "Consulting / agency / engineering services firm — bills hours, ships deliverables. No parts, BOMs, inventory, or shop floor.",
        TargetProfile: "5–50 people doing professional services (consulting, agency, engineering). Engagements are time + deliverable based; revenue tied to billable hours, fixed bids, or retainers.",
        EnabledCapabilities: AssemblePreset(
            // Manufacturing / inventory / shop-floor concepts hidden.
            remove:
            [
                "CAP-MD-PARTS", "CAP-MD-BOM", "CAP-MD-ROUTING", "CAP-MD-WORKCENTERS",
                "CAP-P2P-RECEIVE",
                "CAP-O2C-PICKPACK", "CAP-O2C-SHIP",
                "CAP-MFG-WO-RELEASE", "CAP-MFG-MATL-ISSUE", "CAP-MFG-LABOR",
                "CAP-MFG-MULTIOP", "CAP-MFG-COMPLETE", "CAP-MFG-SHOPFLOOR",
                "CAP-INV-CORE", "CAP-INV-CYCLECOUNT",
                "CAP-RPT-INVVAL", "CAP-RPT-OPERATIONAL",
            ],
            add:
            [
                "CAP-O2C-LEAD", "CAP-O2C-RECURRING", "CAP-O2C-DELIVERABLE",
                "CAP-PS-ENGAGEMENT", "CAP-PS-TIME-BILLABLE", "CAP-PS-RATE-CARDS",
                "CAP-PS-PROJECT-COST", "CAP-PS-UTILIZATION", "CAP-PS-RETAINER",
                "CAP-QC-COMPLIANCE-FORMS", // per D7 — NDAs / MSAs apply to services
            ]),
        // ── Terminology overlay (full) ──
        // Per the agile / scrum mental model: Job becomes Task (the work-
        // tracking primitive); Track Type becomes Task Type (Epic, Project,
        // Story, Bug, Spike — each with its own stage set per the
        // TrackTypeBundle below).
        TerminologyBundle: new TerminologyBundle(
            Labels: new Dictionary<string, string>
            {
                ["entity_job"] = "Task",
                ["entity_track_type"] = "Task Type",
                ["entity_customer"] = "Client",
                ["entity_work_center"] = "Consultant",
                ["entity_planning_cycle"] = "Sprint",
                ["status_in_production"] = "In Progress",
                ["status_shipped"] = "Done",
                ["action_start_production"] = "Start Work",
                ["label_jobs"] = "Tasks",
                ["label_customers"] = "Clients",
            }),
        // ── Reference data (10 service-shop groups) ──
        ReferenceDataBundle: new ReferenceDataBundle(
            Groups: new Dictionary<string, IReadOnlyList<ReferenceDataValueSeed>>
            {
                ["engagement_type"] = new List<ReferenceDataValueSeed>
                {
                    new("consulting",      "Consulting",      1),
                    new("project",         "Project",         2),
                    new("retainer",        "Retainer",        3),
                    new("ongoing_service", "Ongoing Service", 4),
                },
                ["project_phase"] = new List<ReferenceDataValueSeed>
                {
                    new("discovery", "Discovery", 1),
                    new("design",    "Design",    2),
                    new("build",     "Build",     3),
                    new("deliver",   "Deliver",   4),
                    new("maintain",  "Maintain",  5),
                },
                ["time_billable_status"] = new List<ReferenceDataValueSeed>
                {
                    new("billable",     "Billable",             1, "{\"color\":\"#15803d\"}"),
                    new("non_billable", "Non-Billable",         2, "{\"color\":\"#94a3b8\"}"),
                    new("internal",     "Internal",             3),
                    new("travel",       "Travel (non-billable)", 4),
                },
                ["time_activity_type"] = new List<ReferenceDataValueSeed>
                {
                    new("discovery",     "Discovery",     1),
                    new("design",        "Design",        2),
                    new("build",         "Build",         3),
                    new("testing",       "Testing",       4),
                    new("documentation", "Documentation", 5),
                    new("travel",        "Travel",        6),
                    new("admin",         "Admin",         7),
                },
                ["deliverable_type"] = new List<ReferenceDataValueSeed>
                {
                    new("report",        "Report",        1),
                    new("code",          "Code",          2),
                    new("design",        "Design",        3),
                    new("documentation", "Documentation", 4),
                    new("training",      "Training",      5),
                    new("other",         "Other",         6),
                },
                ["service_uom"] = new List<ReferenceDataValueSeed>
                {
                    new("hour",       "Hour",       1),
                    new("day",        "Day",        2),
                    new("week",       "Week",       3),
                    new("sprint",     "Sprint",     4),
                    new("engagement", "Engagement", 5),
                    new("fixed_bid",  "Fixed Bid",  6),
                },
                ["engagement_status"] = new List<ReferenceDataValueSeed>
                {
                    new("proposal", "Proposal", 1),
                    new("won",      "Won",      2),
                    new("active",   "Active",   3),
                    new("paused",   "Paused",   4),
                    new("complete", "Complete", 5),
                    new("lost",     "Lost",     6),
                },
                ["retainer_status"] = new List<ReferenceDataValueSeed>
                {
                    new("active",   "Active",   1),
                    new("expired",  "Expired",  2),
                    new("renewed",  "Renewed",  3),
                },
                ["client_segment"] = new List<ReferenceDataValueSeed>
                {
                    new("enterprise",    "Enterprise",    1),
                    new("mid_market",    "Mid-Market",    2),
                    new("smb",           "SMB",           3),
                    new("public_sector", "Public Sector", 4),
                },
            }),
        // ── Task types (agile / scrum) ──
        // Five task types, each with its own status set tuned to its
        // lifecycle. Project keeps the full O2C-style stage progression
        // (Proposal → ... → Paid) because that's the canonical agency
        // engagement shape and carries the accounting document hooks.
        // The other four are internal-work types with no accounting docs:
        //   Epic    — coarse-grained initiative spanning weeks/months
        //   Story   — user story / feature within an Epic
        //   Bug     — defect lifecycle
        //   Spike   — time-boxed exploratory investigation
        TrackTypeBundle: new TrackTypeBundle(
            TrackTypes: new List<TrackTypeSeed>
            {
                new(
                    Code: "ps_epic",
                    Name: "Epic",
                    SortOrder: 1,
                    IsDefault: false,
                    IsShopFloor: false,
                    Stages: new List<JobStageSeed>
                    {
                        new("draft",       "Draft",       1, "#94a3b8"),
                        new("refined",     "Refined",     2, "#0ea5e9"),
                        new("in_progress", "In Progress", 3, "#f59e0b"),
                        new("review",      "Review",      4, "#ec4899"),
                        new("done",        "Done",        5, "#15803d"),
                    }),
                new(
                    Code: "ps_project",
                    Name: "Project",
                    SortOrder: 2,
                    IsDefault: true,
                    IsShopFloor: false,
                    Stages: new List<JobStageSeed>
                    {
                        new("proposal",  "Proposal",        1, "#94a3b8"),
                        new("won",       "Won",             2, "#0d9488", AccountingDocumentType: AccountingDocumentType.SalesOrder),
                        new("discovery", "Discovery",       3, "#0ea5e9"),
                        new("active",    "Active Delivery", 4, "#f59e0b"),
                        new("review",    "In Review",       5, "#ec4899"),
                        new("delivered", "Delivered",       6, "#15803d"),
                        new("invoiced",  "Invoiced",        7, "#dc2626", AccountingDocumentType: AccountingDocumentType.Invoice, IsIrreversible: true),
                        new("paid",      "Paid",            8, "#16a34a", AccountingDocumentType: AccountingDocumentType.Payment, IsIrreversible: true),
                    }),
                new(
                    Code: "ps_story",
                    Name: "Story",
                    SortOrder: 3,
                    IsDefault: false,
                    IsShopFloor: false,
                    Stages: new List<JobStageSeed>
                    {
                        new("backlog",     "Backlog",     1, "#94a3b8"),
                        new("ready",       "Ready",       2, "#0ea5e9"),
                        new("in_progress", "In Progress", 3, "#f59e0b"),
                        new("review",      "Review",      4, "#ec4899"),
                        new("done",        "Done",        5, "#15803d"),
                    }),
                new(
                    Code: "ps_bug",
                    Name: "Bug",
                    SortOrder: 4,
                    IsDefault: false,
                    IsShopFloor: false,
                    Stages: new List<JobStageSeed>
                    {
                        new("reported",    "Reported",    1, "#94a3b8"),
                        new("triaged",     "Triaged",     2, "#0ea5e9"),
                        new("in_progress", "In Progress", 3, "#f59e0b"),
                        new("review",      "Review",      4, "#ec4899"),
                        new("resolved",    "Resolved",    5, "#15803d"),
                        new("verified",    "Verified",    6, "#16a34a"),
                    }),
                new(
                    Code: "ps_spike",
                    Name: "Spike",
                    SortOrder: 5,
                    IsDefault: false,
                    IsShopFloor: false,
                    Stages: new List<JobStageSeed>
                    {
                        new("defined",     "Defined",     1, "#94a3b8"),
                        new("in_progress", "In Progress", 2, "#f59e0b"),
                        new("outcome",     "Outcome",     3, "#ec4899"),
                        new("closed",      "Closed",      4, "#15803d"),
                    }),
            }),
        // ── Roles (AddOnly default — never strips admin grants) ──
        RoleBundle: new RoleBundle(
            Roles: new List<RoleSeed>
            {
                new("practitioner",       "Practitioner",       Description: "Service practitioner — delivers engagement work, logs billable hours."),
                new("engagement_manager", "Engagement Manager", Description: "Owns one or more engagements end-to-end: client relationship, scope, budget, delivery."),
                new("account_manager",    "Account Manager",    Description: "Owns the client relationship across engagements: sales, renewals, retainer health."),
                new("delivery_lead",      "Delivery Lead",      Description: "Senior delivery role — multi-engagement oversight, escalation point, practice lead."),
            }),
        // ── Folder map suggestions (per D9) ──
        // Used by the dual-path auto-create flow (D2) when an entity is
        // created on a Pro Services install with CAP-EXT-CLOUD-STORAGE
        // enabled. The applier persists this catalog to a single
        // system_setting row; the auto-create flow consults it at entity-
        // create time.
        FolderMapBundle: new FolderMapBundle(
            Suggestions: new List<FolderMapSuggestion>
            {
                new(
                    EntityType: "Customer",
                    PathTemplate: "/Clients/{Customer}/",
                    SubfolderNames: new[] { "00-General", "01-Contracts", "02-Tasks" }),
                new(
                    EntityType: "Job",
                    PathTemplate: "/Clients/{Customer}/02-Tasks/{Job}/",
                    SubfolderNames: new[] { "Proposal", "Contracts", "Discovery", "Working", "Deliverables", "Final" }),
                new(
                    EntityType: "Deliverable",
                    PathTemplate: "/Clients/{Customer}/02-Tasks/{Job}/Deliverables/",
                    SubfolderNames: new[] { "Draft", "Review", "Final" }),
            }));

    public static PresetDefinition Preset09_Hybrid { get; } = new(
        Id: "PRESET-09",
        Name: "Hybrid (Make + Service)",
        ShortDescription: "Shop that both makes products AND sells services (e.g. engineering firm with a fab shop; product company with a services arm). Carries the union of manufacturing + Pro Services capabilities.",
        TargetProfile: "10–100 people doing both manufacturing and services. Both Production AND Engagement track types are active; vocabulary follows Pro Services renames for service-side work.",
        EnabledCapabilities: AssemblePreset(
            // Manufacturing baseline stays. Add Pro Services capabilities on top.
            remove: ["CAP-ACCT-BUILTIN"],  // Hybrid typically uses external accounting (like PRESET-04)
            add:
            [
                "CAP-ACCT-EXTERNAL",
                "CAP-MD-PRICELIST",
                "CAP-P2P-RFQ", "CAP-P2P-SUBCONTRACT",
                "CAP-O2C-LEAD", "CAP-O2C-RECURRING", "CAP-O2C-DELIVERABLE",
                "CAP-O2C-COLLECTIONS", "CAP-O2C-RMA",
                "CAP-PLAN-SAFETYSTOCK",
                "CAP-INV-RESERVE",
                "CAP-QC-INSPECTION", "CAP-QC-NCR", "CAP-QC-COMPLIANCE-FORMS",
                "CAP-IDEN-AUTH-KIOSK", "CAP-EXT-SHOPFLOOR-KIOSK",
                "CAP-HR-LEAVE",
                // Pro Services overlay
                "CAP-PS-ENGAGEMENT", "CAP-PS-TIME-BILLABLE", "CAP-PS-RATE-CARDS",
                "CAP-PS-PROJECT-COST", "CAP-PS-UTILIZATION", "CAP-PS-RETAINER",
            ]),
        // ── Same terminology overlay as PRESET-08 (per user — Hybrid carries
        // full Project/Client/Consultant renames even where manufacturing
        // surfaces would otherwise read with the original vocabulary). ──
        TerminologyBundle: Preset08_ProServices.TerminologyBundle,
        // ── Same reference-data seed as PRESET-08 — manufacturing groups
        // continue to be seeded by SeedData.Essential for now. ──
        ReferenceDataBundle: Preset08_ProServices.ReferenceDataBundle,
        // ── Both track-type sets: Engagement (from PRESET-08) PLUS the
        // manufacturing tracks (Production / R&D / Maintenance) carried by
        // the existing install seeder. Hybrid presets that re-apply find
        // existing manufacturing track types via UpsertByCode and leave
        // them alone — the bundle adds Engagement without disturbing them. ──
        TrackTypeBundle: Preset08_ProServices.TrackTypeBundle,
        // ── Same Pro Services role seed as PRESET-08; manufacturing roles
        // carried by the existing seeder. ──
        RoleBundle: Preset08_ProServices.RoleBundle,
        // ── Same folder map as PRESET-08; Hybrid installs benefit from the
        // services-shaped folder layout for engagement-track Jobs. ──
        FolderMapBundle: Preset08_ProServices.FolderMapBundle);

    public static PresetDefinition PresetCustom { get; } = new(
        Id: "PRESET-CUSTOM",
        Name: "Custom",
        ShortDescription: "Empty starting point — build the capability set yourself. Reachable from any point in discovery via the exit ramp.",
        TargetProfile: "Any business shape that doesn't fit one of the named presets, or any operator who wants explicit control.",
        // Per 4B Open Question 5 / Phase F decision: Custom inherits the 41
        // catalog defaults at apply-time (handled by the apply handler; the
        // catalog list is empty here so consumers can detect "Custom = no
        // preset-driven set").
        EnabledCapabilities: [],
        IsCustom: true);

    public static IReadOnlyList<PresetDefinition> All { get; } = new List<PresetDefinition>
    {
        Preset01_TwoPersonShop,
        Preset02_GrowingJobShop,
        Preset03_Distribution,
        Preset04_ProductionManufacturer,
        Preset05_RegulatedManufacturer,
        Preset06_MultiSite,
        Preset07_Enterprise,
        Preset08_ProServices,
        Preset09_Hybrid,
        PresetCustom,
    };

    public static PresetDefinition? FindById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}
