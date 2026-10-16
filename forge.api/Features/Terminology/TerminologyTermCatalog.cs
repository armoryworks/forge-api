namespace Forge.Api.Features.Terminology;

public static class TerminologyTermCatalog
{
    public static readonly IReadOnlyList<TerminologyTerm> All =
    [
        new("job", "jobs"),
        new("customer", "customers"),
        new("vendor", "vendors"),
        new("part", "parts"),
        new("work_center", "work_centers"),
        new("quote", "quotes"),
        new("sales_order", "sales_orders"),
        new("purchase_order", "purchase_orders"),
        new("invoice", "invoices"),
        new("shipment", "shipments"),
        new("lead", "leads"),
        new("employee", "employees"),
        new("asset", "assets"),
        new("planning_cycle", "planning_cycles"),
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
