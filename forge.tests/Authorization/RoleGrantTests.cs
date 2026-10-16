using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// Read grants that UI pages depend on. ASP.NET Core combines every <c>[Authorize(Roles)]</c> on a
/// controller and its action by AND, so these tests resolve the effective role set the way the
/// framework does rather than trusting the action attribute alone.
/// </summary>
public sealed class RoleGrantTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "IT Admin", "Procurement", "Production Manager", "Production Planner",
        "ComplianceOfficer",
    ];

    private static IEnumerable<Type> Controllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<(Type Controller, MethodInfo Action, string Template)> Endpoints(string verb) =>
        from controller in Controllers()
        let prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        from http in action.GetCustomAttributes<HttpMethodAttribute>()
        where http.HttpMethods.Contains(verb)
        let template = http.Template is null ? prefix : prefix is null ? http.Template : $"{prefix}/{http.Template}"
        select (controller, action, template ?? string.Empty);

    private static MethodInfo Single(string verb, string template)
    {
        var matches = Endpoints(verb).Where(e => e.Template == template).ToList();
        matches.Should().ContainSingle($"{verb} {template} must resolve to exactly one action");
        return matches[0].Action;
    }

    private static HashSet<string> EffectiveRoles(MethodInfo action)
    {
        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        var attributes = action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>());
        foreach (var attribute in attributes.Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Manager", true)]
    [InlineData("OfficeManager", false)]
    [InlineData("ProductionWorker", false)]
    public void Role_catalog_is_readable_by_managers(string role, bool admitted)
    {
        var action = Single("GET", "api/v1/admin/roles");

        EffectiveRoles(action).Contains(role).Should().Be(admitted);
        action.GetCustomAttribute<CapabilityBootstrapAttribute>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("ProductionWorker", true)]
    [InlineData("Engineer", true)]
    [InlineData("PM", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Procurement", false)]
    public void Customer_dropdown_is_readable_from_the_kanban_board(string role, bool admitted)
    {
        var action = Single("GET", "api/v1/customers/dropdown");

        EffectiveRoles(action).Contains(role).Should().Be(admitted);
        action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability.Should().Be("CAP-MD-CUSTOMERS");
    }

    [Fact]
    public void The_customer_dropdown_grant_does_not_widen_the_rest_of_the_customer_surface()
    {
        var list = Single("GET", "api/v1/customers");

        EffectiveRoles(list).Should().NotContain("ProductionWorker");
    }

    [Fact]
    public void Expense_policy_is_readable_by_every_role_that_files_expenses()
    {
        EffectiveRoles(Single("GET", "api/v1/expenses/settings")).Should().BeEquivalentTo(AllRoles);
        EffectiveRoles(Single("PUT", "api/v1/expenses/settings")).Should().BeEquivalentTo(["Admin"]);
    }

    [Fact]
    public void Company_locations_are_readable_by_purchase_order_roles_and_writable_by_admin_only()
    {
        EffectiveRoles(Single("GET", "api/v1/company-locations"))
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Procurement"]);
        EffectiveRoles(Single("GET", "api/v1/company-locations/{id:int}"))
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Procurement"]);

        foreach (var (verb, template) in new[]
        {
            ("POST", "api/v1/company-locations"),
            ("PUT", "api/v1/company-locations/{id:int}"),
            ("DELETE", "api/v1/company-locations/{id:int}"),
            ("POST", "api/v1/company-locations/{id:int}/set-default"),
        })
        {
            EffectiveRoles(Single(verb, template)).Should().BeEquivalentTo(["Admin"], $"{verb} {template}");
        }
    }

    [Fact]
    public void Recalls_are_readable_by_engineers_and_raised_or_resolved_by_managers()
    {
        EffectiveRoles(Single("GET", "api/v1/recalls"))
            .Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        EffectiveRoles(Single("GET", "api/v1/recalls/{id:int}"))
            .Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        EffectiveRoles(Single("POST", "api/v1/recalls"))
            .Should().BeEquivalentTo(["Admin", "Manager"]);
        EffectiveRoles(Single("POST", "api/v1/recalls/{id:int}/resolve"))
            .Should().BeEquivalentTo(["Admin", "Manager"]);
    }

    [Fact]
    public void Gl_reads_stay_controller_only()
    {
        EffectiveRoles(Single("GET", "api/v1/accounting/pnl")).Should().BeEquivalentTo(["Controller"]);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Manager", true)]
    [InlineData("OfficeManager", false)]
    public void Role_templates_and_shift_assignments_are_readable_by_managers(string role, bool admitted)
    {
        EffectiveRoles(Single("GET", "api/v1/admin/role-templates")).Contains(role).Should().Be(admitted);
        EffectiveRoles(Single("GET", "api/v1/admin/shift-assignments")).Contains(role).Should().Be(admitted);
        EffectiveRoles(Single("POST", "api/v1/admin/role-templates")).Should().BeEquivalentTo(["Admin"]);
        EffectiveRoles(Single("POST", "api/v1/admin/shift-assignments")).Should().BeEquivalentTo(["Admin"]);
    }

    [Theory]
    [InlineData("Controller", true)]
    [InlineData("PM", true)]
    [InlineData("ProductionWorker", false)]
    public void Job_cost_summary_is_readable_by_the_controller_and_not_by_production_workers(string role, bool admitted)
    {
        EffectiveRoles(Single("GET", "api/v1/jobs/{id:int}/cost-summary")).Contains(role).Should().Be(admitted);
    }

    [Theory]
    [InlineData("GET", "api/v1/vendors")]
    [InlineData("GET", "api/v1/vendors/{id:int}")]
    [InlineData("POST", "api/v1/vendors")]
    [InlineData("PUT", "api/v1/vendors/{id:int}")]
    [InlineData("GET", "api/v1/vendors/{vendorId:int}/contacts")]
    [InlineData("POST", "api/v1/vendors/{vendorId:int}/contacts")]
    [InlineData("GET", "api/v1/vendors/{vendorId:int}/addresses")]
    [InlineData("POST", "api/v1/vendors/{vendorId:int}/addresses")]
    [InlineData("POST", "api/v1/vendor-parts")]
    [InlineData("GET", "api/v1/purchasing/rfqs")]
    [InlineData("POST", "api/v1/purchasing/rfqs")]
    [InlineData("POST", "api/v1/purchasing/rfqs/{id:int}/send")]
    [InlineData("POST", "api/v1/purchasing/rfqs/{id:int}/award/{responseId:int}")]
    [InlineData("GET", "api/v1/purchase-orders")]
    [InlineData("GET", "api/v1/purchase-orders/{id:int}")]
    [InlineData("POST", "api/v1/purchase-orders")]
    [InlineData("POST", "api/v1/purchase-orders/{id:int}/submit")]
    [InlineData("POST", "api/v1/purchase-orders/{id:int}/receive")]
    [InlineData("POST", "api/v1/purchase-orders/{id:int}/short-close")]
    [InlineData("GET", "api/v1/purchase-orders/calendar")]
    [InlineData("GET", "api/v1/auto-po/suggestions")]
    [InlineData("POST", "api/v1/auto-po/suggestions/{id:int}/convert")]
    [InlineData("GET", "api/v1/jobs")]
    [InlineData("GET", "api/v1/parts")]
    [InlineData("GET", "api/v1/parts/{partId:int}/purchase-units")]
    [InlineData("GET", "api/v1/company-locations")]
    public void Procurement_runs_purchasing(string verb, string template)
    {
        EffectiveRoles(Single(verb, template)).Should().Contain("Procurement", $"{verb} {template}");
    }

    [Theory]
    [InlineData("POST", "api/v1/auto-po/run")]
    [InlineData("GET", "api/v1/auto-po/settings")]
    [InlineData("POST", "api/v1/purchase-orders/backfill-origins")]
    [InlineData("POST", "api/v1/company-locations")]
    public void Purchasing_administration_stays_with_admin(string verb, string template)
    {
        EffectiveRoles(Single(verb, template)).Should().BeEquivalentTo(["Admin"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("POST", "api/v1/parts")]
    [InlineData("PATCH", "api/v1/parts/{id:int}")]
    [InlineData("GET", "api/v1/parts/{id:int}")]
    public void The_parts_list_grant_does_not_open_the_rest_of_the_parts_surface_to_procurement(string verb, string template)
    {
        EffectiveRoles(Single(verb, template)).Should().NotContain("Procurement", $"{verb} {template}");
    }

    [Theory]
    [InlineData("POST", "api/v1/parts/{partId:int}/purchase-units")]
    [InlineData("PUT", "api/v1/parts/{partId:int}/purchase-units/{id:int}")]
    [InlineData("DELETE", "api/v1/parts/{partId:int}/purchase-units/{id:int}")]
    public void Part_purchase_units_are_still_maintained_by_the_part_master_roles(string verb, string template)
    {
        EffectiveRoles(Single(verb, template)).Should().BeEquivalentTo(
            ["Admin", "Manager", "Engineer", "OfficeManager"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("POST", "api/v1/jobs")]
    [InlineData("GET", "api/v1/jobs/{id:int}")]
    public void The_job_list_grant_does_not_open_the_rest_of_the_job_surface_to_procurement(string verb, string template)
    {
        EffectiveRoles(Single(verb, template)).Should().NotContain("Procurement", $"{verb} {template}");
    }

    [Fact]
    public void The_job_list_keeps_every_role_that_read_it_before()
    {
        EffectiveRoles(Single("GET", "api/v1/jobs")).Should().BeEquivalentTo(
            ["Admin", "Manager", "PM", "Engineer", "ProductionWorker", "OfficeManager", "Procurement"]);
    }

    [Fact]
    public void The_parts_list_keeps_every_role_that_read_it_before()
    {
        EffectiveRoles(Single("GET", "api/v1/parts")).Should().BeEquivalentTo(
            ["Admin", "Manager", "Engineer", "ProductionWorker", "PM", "OfficeManager", "Procurement"]);
    }

    [Fact]
    public void Vendor_bills_are_read_by_procurement_and_the_controller_and_written_by_ap_roles()
    {
        foreach (var template in new[] { "api/v1/vendor-bills", "api/v1/vendor-bills/{id:int}" })
        {
            EffectiveRoles(Single("GET", template)).Should().BeEquivalentTo(
                ["Admin", "Manager", "OfficeManager", "Procurement", "Controller"], template);
        }

        foreach (var template in new[] { "api/v1/vendor-bills", "api/v1/vendor-bills/{id:int}/approve", "api/v1/vendor-bills/{id:int}/void" })
        {
            EffectiveRoles(Single("POST", template)).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"], template);
        }
    }

    [Fact]
    public void Vendor_payments_and_transmissions_are_read_by_the_controller_and_written_by_ap_roles()
    {
        foreach (var template in new[] { "api/v1/vendor-payments", "api/v1/vendor-payments/{id:int}", "api/v1/payment-transmissions" })
        {
            EffectiveRoles(Single("GET", template)).Should().BeEquivalentTo(
                ["Admin", "Manager", "OfficeManager", "Controller"], template);
        }

        foreach (var template in new[] { "api/v1/vendor-payments", "api/v1/vendor-payments/{id:int}/void", "api/v1/payment-transmissions/{id:int}/retry" })
        {
            EffectiveRoles(Single("POST", template)).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"], template);
        }
    }

    [Fact]
    public void The_vendor_dropdown_serves_the_payables_filter_for_the_controller()
    {
        EffectiveRoles(Single("GET", "api/v1/vendors/dropdown")).Should().BeEquivalentTo(
            ["Admin", "Manager", "OfficeManager", "Procurement", "Controller"]);
        EffectiveRoles(Single("GET", "api/v1/vendors")).Should().NotContain("Controller");
    }
}
