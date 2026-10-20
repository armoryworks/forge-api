namespace Forge.Api.Features.Terminology;

public static class TerminologyTermCatalog
{
    public static readonly IReadOnlyList<TerminologyTerm> All =
    [
        new("job", "jobs", "Job", "Jobs"),
        new("customer", "customers", "Customer", "Customers"),
        new("vendor", "vendors", "Vendor", "Vendors"),
        new("part", "parts", "Part", "Parts"),
        new("work_center", "work_centers", "Work Center", "Work Centers"),
        new("quote", "quotes", "Quote", "Quotes"),
        new("sales_order", "sales_orders", "Sales Order", "Sales Orders"),
        new("purchase_order", "purchase_orders", "Purchase Order", "Purchase Orders"),
        new("invoice", "invoices", "Invoice", "Invoices"),
        new("shipment", "shipments", "Shipment", "Shipments"),
        new("lead", "leads", "Lead", "Leads"),
        new("employee", "employees", "Employee", "Employees"),
        new("asset", "assets", "Asset", "Assets"),
        new("planning_cycle", "planning_cycles", "Planning Cycle", "Planning Cycles"),
    ];

    private static readonly HashSet<string> TermKeys = All
        .SelectMany(t => new[] { t.SingularKey, t.PluralKey, t.GenderKey })
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsTermKey(string key)
    {
        var separator = key.IndexOf(':');
        return TermKeys.Contains(separator < 0 ? key : key[..separator]);
    }
}
