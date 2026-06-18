namespace Forge.Api.Capabilities;

/// <summary>
/// A customer-facing "module" for the first-run module picker — a plain-language
/// grouping of capabilities a shop turns on as a unit ("Do you want to use X").
/// The picker and the admin screen share this one source (see
/// docs/modular-onboarding.md). Each module names its headline capabilities; the
/// real enabled set is expanded to the dependency closure at apply time, so a
/// module never has to spell out its prerequisites.
/// </summary>
public record ModuleDefinition(
    string Id,
    string Name,
    string Summary,
    string PrerequisiteNote,
    IReadOnlyList<string> Capabilities,
    bool DefaultSelected = false);

/// <summary>
/// The modules offered by the first-run picker, plus the always-on foundation set
/// (the "basic items are always required" from the onboarding design). A picker
/// selection resolves to: Foundations ∪ (selected modules) ∪ dependency closure.
/// Everything else is turned off, which is what makes a single-module install
/// (e.g. Inventory only) genuinely cordoned without being a walled garden.
/// </summary>
public static class ModuleCatalog
{
    /// <summary>Always on — identity, cross-cutting UX, core master data, basic reporting.</summary>
    public static IReadOnlyList<string> Foundations { get; } = new[]
    {
        // Identity + access
        "CAP-IDEN-AUTH-PASSWORD", "CAP-IDEN-USERS", "CAP-IDEN-ROLES",
        "CAP-IDEN-TENANT-CONFIG", "CAP-IDEN-AUDIT-SYSTEM-LOG", "CAP-IDEN-CAPABILITY-ADMIN",
        // Cross-cutting platform UX
        "CAP-CROSS-PERMS-MATRIX", "CAP-CROSS-ACTIVITY-LOG", "CAP-CROSS-LIST-UX",
        "CAP-CROSS-BULK-OPS", "CAP-CROSS-DOCS", "CAP-CROSS-ATTACHMENTS",
        "CAP-CROSS-NOTIFICATIONS", "CAP-CROSS-INTEG-FILE", "CAP-CROSS-CONCURRENCY",
        // Core master data every flow leans on
        "CAP-MD-PARTS", "CAP-MD-UOM", "CAP-MD-LOCATIONS", "CAP-MD-CURRENCIES", "CAP-MD-TAXCODES",
        // Baseline dashboards + mobile shell. Operational reports (CAP-RPT-OPERATIONAL)
        // are deliberately NOT here: they require customer + vendor master data, so a
        // foundations slot would force Customers/Vendors on for every install (e.g.
        // Inventory only). They belong with the modules that own that data instead.
        "CAP-RPT-DASHBOARDS", "CAP-EXT-MOBILE",
    };

    public static IReadOnlyList<ModuleDefinition> All { get; } = new List<ModuleDefinition>
    {
        new("inventory", "Inventory",
            "Track what you have on hand — receive, use, move, and count stock.",
            "Turns on part records and storage locations.",
            new[] { "CAP-INV-CORE", "CAP-INV-CYCLECOUNT", "CAP-INV-ADJUST", "CAP-RPT-INVVAL" },
            DefaultSelected: true),

        // Vendors live here: they're the procure-to-pay anchor (no vendor, no PO or
        // receipt). ⚡ Accounting boundary: vendors are shared master data — the AP
        // side (CAP-P2P-BILL / CAP-P2P-PAY, vendor bills + payments) also uses them
        // and is accounting-bounded: full local CRUD in standalone mode, read-only
        // when an external accounting provider is connected. (Customers mirror this
        // under Sales for the AR side.)
        new("purchasing", "Purchasing",
            "Order materials from vendors and receive them into stock.",
            "Turns on vendor records and receiving; receiving adds to Inventory.",
            new[] { "CAP-MD-VENDORS", "CAP-P2P-PO", "CAP-P2P-RECEIVE", "CAP-P2P-BILL", "CAP-P2P-PAY" }),

        new("sales", "Sales and quoting",
            "Quote customers and turn quotes into sales orders.",
            "Turns on customer records, contacts, and addresses.",
            new[] { "CAP-MD-CUSTOMERS", "CAP-MD-CUSTOMER-CONTACTS", "CAP-MD-CUSTOMER-ADDRESSES",
                    "CAP-O2C-QUOTE", "CAP-O2C-SO" }),

        new("production", "Production",
            "Build products from a bill of materials with routings and a shop-floor board.",
            "Turns on BOMs, routings, work centers, and the job board.",
            new[] { "CAP-MD-BOM", "CAP-MD-ROUTING", "CAP-MD-WORKCENTERS", "CAP-MD-CALENDARS",
                    "CAP-MFG-WO-RELEASE", "CAP-MFG-MATL-ISSUE", "CAP-MFG-LABOR", "CAP-MFG-MULTIOP",
                    "CAP-MFG-COMPLETE", "CAP-MFG-SHOPFLOOR", "CAP-EXT-KANBAN" }),

        new("shipping", "Shipping",
            "Pick, pack, and ship orders, with tracking.",
            "Pulls in the parts of the order flow needed to ship.",
            new[] { "CAP-O2C-PICKPACK", "CAP-O2C-SHIP" }),

        new("invoicing", "Invoicing",
            "Invoice customers and record payments in Forge.",
            "Turns on built-in invoicing and payment recording.",
            new[] { "CAP-O2C-INVOICE", "CAP-O2C-CASH", "CAP-ACCT-BUILTIN", "CAP-ACCT-EXPENSES" }),

        new("quality", "Quality",
            "Inspect incoming and produced goods and log nonconformances.",
            "Adds inspections and nonconformance records.",
            new[] { "CAP-QC-INSPECTION", "CAP-QC-NCR" }),

        new("planning", "Planning and scheduling",
            "Plan material and capacity — MRP, master schedule, and available-to-promise.",
            "Pulls in the demand and supply records planning runs against.",
            new[] { "CAP-PLAN-MRP", "CAP-PLAN-MPS", "CAP-PLAN-CAPACITY", "CAP-PLAN-ATP", "CAP-RPT-MRPEX" }),

        new("people", "People",
            "Manage employees, hiring, time tracking, and training.",
            "Turns on employee records.",
            new[] { "CAP-MD-EMPLOYEES", "CAP-HR-HIRE", "CAP-HR-TERMINATION", "CAP-HR-TIMETRACK", "CAP-HR-TRAINING" }),
    };

    public static ModuleDefinition? FindById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the full set of capability codes to enable for the chosen modules:
    /// Foundations ∪ the selected modules' headline capabilities, then expanded so
    /// every prerequisite (per the dependency graph) is included. Unknown module
    /// ids are ignored. The result is dependency-complete, so applying it as the
    /// enabled set passes the capability gate's validation.
    /// </summary>
    public static IReadOnlySet<string> EnabledCapabilitiesFor(IEnumerable<string> selectedModuleIds)
    {
        var target = new HashSet<string>(Foundations, StringComparer.Ordinal);
        foreach (var id in selectedModuleIds)
        {
            var module = FindById(id);
            if (module is null) continue;
            foreach (var cap in module.Capabilities) target.Add(cap);
        }
        return ExpandToDependencyClosure(target);
    }

    // Pull in every prerequisite transitively: if X is enabled and X requires Y,
    // Y must be enabled too. Walks the dependency edges to a fixpoint.
    private static IReadOnlySet<string> ExpandToDependencyClosure(HashSet<string> seed)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in CapabilityCatalogRelations.Dependencies)
            {
                if (seed.Contains(edge.From) && seed.Add(edge.To))
                    changed = true;
            }
        }
        return seed;
    }
}
