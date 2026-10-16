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
}
