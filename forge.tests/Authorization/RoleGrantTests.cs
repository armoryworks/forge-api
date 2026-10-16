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
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);
        EffectiveRoles(Single("GET", "api/v1/company-locations/{id:int}"))
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);

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
}
