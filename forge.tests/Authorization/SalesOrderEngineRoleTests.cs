using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;

namespace Forge.Tests.Authorization;

/// <summary>
/// The roles and capabilities of the CPQ, drop-ship, back-to-back and staged-schedule endpoints, resolved the way
/// ASP.NET Core combines controller and action <c>[Authorize(Roles)]</c> (AND) and the capability gate reads the
/// most specific attribute.
/// </summary>
public sealed class SalesOrderEngineRoleTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "IT Admin", "Procurement", "Production Manager", "Production Planner",
        "ComplianceOfficer",
    ];

    private static MethodInfo Action(string verb, string template)
    {
        var matches =
            (from controller in typeof(Program).Assembly.GetTypes()
             where controller is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(controller)
             let prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template
             from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             from http in action.GetCustomAttributes<HttpMethodAttribute>()
             where http.HttpMethods.Contains(verb)
                 && (http.Template is null ? prefix : $"{prefix}/{http.Template}") == template
             select action).ToList();
        matches.Should().ContainSingle($"{verb} {template} must resolve to exactly one action");
        return matches[0];
    }

    private static HashSet<string> Roles(MethodInfo action)
    {
        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        foreach (var attribute in action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
                     .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                     .Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    private static string? Capability(MethodInfo action)
        => (action.GetCustomAttribute<RequiresCapabilityAttribute>() ?? action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>())?.Capability;

    [Theory]
    [InlineData("POST", "api/v1/sales-orders/{soId:int}/lines/{lineId:int}/drop-ship", "CAP-P2P-DROPSHIP")]
    [InlineData("POST", "api/v1/purchase-orders/{poId:int}/lines/{lineId:int}/drop-ship-confirm", "CAP-P2P-DROPSHIP")]
    [InlineData("GET", "api/v1/drop-ships/pending", "CAP-P2P-DROPSHIP")]
    [InlineData("POST", "api/v1/sales-orders/{soId:int}/lines/{lineId:int}/back-to-back", "CAP-P2P-BACKTOBACK")]
    [InlineData("POST", "api/v1/purchase-orders/{poId:int}/lines/{lineId:int}/link-receipt", "CAP-P2P-BACKTOBACK")]
    [InlineData("GET", "api/v1/back-to-back/pending", "CAP-P2P-BACKTOBACK")]
    public void Linked_purchasing_admits_the_sales_office_and_procurement(string verb, string template, string capability)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Engineer", "Procurement"]);
        Roles(action).Should().NotContain(["ProductionWorker", "PM", "Controller"]);
        Capability(action).Should().Be(capability);
    }

    [Theory]
    [InlineData("GET", "api/v1/cpq/configurators")]
    [InlineData("GET", "api/v1/cpq/configurators/{id:int}")]
    [InlineData("POST", "api/v1/cpq/configure")]
    [InlineData("POST", "api/v1/cpq/validate")]
    [InlineData("POST", "api/v1/cpq/configurations")]
    [InlineData("POST", "api/v1/cpq/configurations/{id:int}/generate-quote")]
    [InlineData("POST", "api/v1/cpq/configurations/{id:int}/generate-part")]
    public void Configuring_follows_the_sales_office_roles(string verb, string template)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);
        Roles(action).Should().NotContain(["Engineer", "ProductionWorker"]);
        Capability(action).Should().Be("CAP-O2C-CPQ");
    }

    [Theory]
    [InlineData("POST", "api/v1/cpq/configurators")]
    [InlineData("PUT", "api/v1/cpq/configurators/{id:int}")]
    public void Authoring_a_configurator_is_for_admins_and_managers(string verb, string template)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager"]);
        Roles(action).Should().NotContain("OfficeManager");
    }

    [Theory]
    [InlineData("POST", "api/v1/orders/{salesOrderId:int}/stages")]
    [InlineData("PUT", "api/v1/sales-order-stages/{id:int}")]
    [InlineData("DELETE", "api/v1/sales-order-stages/{id:int}")]
    [InlineData("PUT", "api/v1/sales-order-stages/{id:int}/lines")]
    public void Editing_stages_follows_the_sales_order_roles(string verb, string template)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);
        Roles(action).Should().NotContain("Procurement");
        Capability(action).Should().Be("CAP-O2C-SO");
    }
}
