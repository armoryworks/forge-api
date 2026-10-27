using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Authorization;
using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// S03-08: the Production Planner plans (MRP, scheduling, planning cycles) and reads what planning needs (jobs,
/// stock, parts, BOMs, routings, work centers, purchase and sales orders) without changing master data, money or
/// people. Roles are resolved the way ASP.NET Core does: every <c>[Authorize(Roles)]</c> on the controller and the
/// action must admit the role.
/// </summary>
public sealed class PlannerRoleGrantTests
{
    private const string Planner = "Production Planner";

    private static IEnumerable<Type> Controllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<(Type Controller, MethodInfo Action, string Verb, string Template)> Endpoints() =>
        from controller in Controllers()
        let prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        from http in action.GetCustomAttributes<HttpMethodAttribute>()
        from verb in http.HttpMethods
        let template = http.Template is null ? prefix : prefix is null ? http.Template : $"{prefix}/{http.Template}"
        select (controller, action, verb, template ?? string.Empty);

    private static bool Admits(MethodInfo action, string role)
    {
        var attributes = action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
            .Where(a => !string.IsNullOrEmpty(a.Roles));
        return attributes.All(a => a.Roles!.Split(',').Select(r => r.Trim()).Contains(role));
    }

    private static List<MethodInfo> Actions(string verb, string template)
    {
        var matches = Endpoints().Where(e => e.Verb == verb && e.Template == template).Select(e => e.Action).ToList();
        matches.Should().NotBeEmpty($"{verb} {template} must exist");
        return matches;
    }

    [Theory]
    [InlineData("GET", "api/v1/mrp/runs")]
    [InlineData("POST", "api/v1/mrp/runs")]
    [InlineData("GET", "api/v1/mrp/planned-orders")]
    [InlineData("POST", "api/v1/mrp/planned-orders/{id:int}/release")]
    [InlineData("POST", "api/v1/mrp/planned-orders/bulk-release")]
    [InlineData("GET", "api/v1/mrp/exceptions")]
    [InlineData("POST", "api/v1/mrp/exceptions/{id:int}/resolve")]
    [InlineData("POST", "api/v1/scheduling/run")]
    [InlineData("POST", "api/v1/scheduling/simulate")]
    [InlineData("GET", "api/v1/scheduling/gantt")]
    [InlineData("GET", "api/v1/scheduling/dispatch/{workCenterId:int}")]
    [InlineData("PATCH", "api/v1/scheduling/operations/{id:int}")]
    [InlineData("GET", "api/v1/shifts")]
    [InlineData("GET", "api/v1/working-calendars")]
    [InlineData("GET", "api/v1/planning-cycles")]
    [InlineData("POST", "api/v1/planning-cycles")]
    [InlineData("POST", "api/v1/planning-cycles/{id:int}/entries")]
    [InlineData("GET", "api/v1/jobs")]
    [InlineData("GET", "api/v1/customers/dropdown")]
    [InlineData("GET", "api/v1/jobs/{id:int}")]
    [InlineData("GET", "api/v1/inventory/parts")]
    [InlineData("GET", "api/v1/inventory/locations")]
    [InlineData("GET", "api/v1/inventory/atp/{partId:int}")]
    [InlineData("GET", "api/v1/inventory/abc/summary")]
    [InlineData("GET", "api/v1/parts")]
    [InlineData("GET", "api/v1/parts/{id:int}")]
    [InlineData("GET", "api/v1/parts/{id:int}/bom/revisions")]
    [InlineData("GET", "api/v1/parts/{id:int}/operations")]
    [InlineData("GET", "api/v1/parts/{id:int}/routing/graph")]
    [InlineData("GET", "api/v1/work-centers")]
    [InlineData("GET", "api/v1/purchase-orders")]
    [InlineData("GET", "api/v1/purchase-orders/{id:int}")]
    [InlineData("GET", "api/v1/sales-orders")]
    [InlineData("GET", "api/v1/orders/{id:int}")]
    [InlineData("GET", "api/v1/orders/{id:int}/schedule")]
    public void Planner_is_admitted(string verb, string template)
    {
        Actions(verb, template).Should().OnlyContain(a => Admits(a, Planner), $"{verb} {template}");
    }

    [Theory]
    [InlineData("GET", "api/v1/price-lists")]
    [InlineData("GET", "api/v1/parts/{id:int}/prices")]
    [InlineData("GET", "api/v1/parts/{id:int}/landed-cost")]
    [InlineData("GET", "api/v1/accounting/accounts")]
    [InlineData("GET", "api/v1/orders/{id:int}/invoices")]
    [InlineData("POST", "api/v1/parts")]
    [InlineData("PATCH", "api/v1/parts/{id:int}")]
    [InlineData("POST", "api/v1/inventory/receive")]
    [InlineData("POST", "api/v1/inventory/abc/run")]
    [InlineData("POST", "api/v1/purchase-orders")]
    [InlineData("POST", "api/v1/purchase-orders/{id:int}/submit")]
    [InlineData("POST", "api/v1/orders")]
    [InlineData("PUT", "api/v1/jobs/{id:int}")]
    [InlineData("POST", "api/v1/shifts")]
    [InlineData("POST", "api/v1/working-calendars/{id:int}/holidays")]
    [InlineData("POST", "api/v1/work-centers")]
    [InlineData("POST", "api/v1/jobs/{id:int}/material-issues")]
    [InlineData("POST", "api/v1/jobs/{id:int}/material-issues/{issueId:int}/return")]
    [InlineData("POST", "api/v1/jobs/{id:int}/production-runs/{runId:int}/receive-to-stock")]
    public void Planner_is_refused(string verb, string template)
    {
        Actions(verb, template).Should().OnlyContain(a => !Admits(a, Planner), $"{verb} {template}");
    }

    [Theory]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(PartsController))]
    [InlineData(typeof(PartListController))]
    [InlineData(typeof(PurchaseOrdersController))]
    [InlineData(typeof(SalesOrdersController))]
    [InlineData(typeof(SalesOrdersListController))]
    [InlineData(typeof(AbcClassificationController))]
    [InlineData(typeof(ShiftsController))]
    [InlineData(typeof(WorkingCalendarsController))]
    [InlineData(typeof(WorkCentersController))]
    public void Planner_only_reads_master_data_and_orders(Type controller)
    {
        var writes = Endpoints()
            .Where(e => e.Controller == controller && e.Verb != "GET" && Admits(e.Action, Planner))
            .Select(e => $"{e.Verb} {e.Template}")
            .ToList();

        writes.Should().BeEmpty("a write action added to a planner-readable controller must carry its own role set");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    [InlineData("Engineer")]
    [InlineData("ProductionWorker")]
    public void Splitting_inventory_reads_from_writes_keeps_every_existing_writer(string role)
    {
        Actions("POST", "api/v1/inventory/receive").Should().OnlyContain(a => Admits(a, role));
        Actions("POST", "api/v1/inventory/bin-contents").Should().OnlyContain(a => Admits(a, role));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    [InlineData("Procurement")]
    public void Splitting_purchase_order_reads_from_writes_keeps_every_existing_buyer(string role)
    {
        Actions("POST", "api/v1/purchase-orders").Should().OnlyContain(a => Admits(a, role));
        Actions("POST", "api/v1/purchase-orders/{id:int}/receive").Should().OnlyContain(a => Admits(a, role));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("PM")]
    [InlineData("Engineer")]
    [InlineData("ProductionWorker")]
    [InlineData("OfficeManager")]
    public void Keeping_the_planner_off_job_stock_moves_keeps_every_existing_job_role(string role)
    {
        Actions("POST", "api/v1/jobs/{id:int}/material-issues").Should().OnlyContain(a => Admits(a, role));
        Actions("POST", "api/v1/jobs/{id:int}/material-issues/{issueId:int}/return").Should().OnlyContain(a => Admits(a, role));
        Actions("POST", "api/v1/jobs/{id:int}/production-runs/{runId:int}/receive-to-stock").Should().OnlyContain(a => Admits(a, role));
    }

    [Fact]
    public void Planner_role_name_matches_the_seeded_role()
    {
        PlanningRoles.Planner.Should().Be(Planner);
    }
}
