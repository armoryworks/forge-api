using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;

namespace Forge.Tests.Authorization;

/// <summary>
/// The endpoints behind the inventory screens (transfers, reservations, ATP, ABC, lots, serials, replenishment
/// cards): each pins an admitted and a refused role, and the capability the screen is gated on.
/// </summary>
public sealed class InventoryScreensEndpointGateTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "Procurement",
    ];

    private static MethodInfo Action(string verb, string template)
    {
        var matches = (
            from controller in typeof(Program).Assembly.GetTypes()
            where controller is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(controller)
            let prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template
            from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            from http in action.GetCustomAttributes<HttpMethodAttribute>()
            where http.HttpMethods.Contains(verb)
            let full = http.Template is null ? prefix : prefix is null ? http.Template : $"{prefix}/{http.Template}"
            where full == template
            select action).ToList();
        matches.Should().ContainSingle($"{verb} {template} must resolve to exactly one action");
        return matches[0];
    }

    private static HashSet<string> EffectiveRoles(MethodInfo action)
    {
        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        foreach (var attribute in action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
                     .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                     .Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    private static HashSet<string> Capabilities(MethodInfo action)
        => action.DeclaringType!.GetCustomAttributes<RequiresCapabilityAttribute>()
            .Concat(action.GetCustomAttributes<RequiresCapabilityAttribute>())
            .Select(a => a.Capability)
            .ToHashSet(StringComparer.Ordinal);

    [Theory]
    [InlineData("GET", "api/v1/inventory/transfers/plants", "ProductionWorker", "PM", "CAP-INV-MULTILOC")]
    [InlineData("POST", "api/v1/inventory/transfers/{id:int}/cancel", "OfficeManager", "ProductionWorker", "CAP-INV-MULTILOC")]
    [InlineData("POST", "api/v1/inventory/transfers", "Engineer", "ProductionWorker", "CAP-INV-MULTILOC")]
    [InlineData("POST", "api/v1/inventory/transfers/{id:int}/receive", "Manager", "Procurement", "CAP-INV-MULTILOC")]
    [InlineData("GET", "api/v1/inventory/reservations/sales-order-lines", "OfficeManager", "PM", "CAP-INV-RESERVE")]
    [InlineData("POST", "api/v1/inventory/reservations", "Engineer", "Controller", "CAP-INV-RESERVE")]
    [InlineData("GET", "api/v1/inventory/atp/{partId:int}", "Engineer", "PM", "CAP-PLAN-ATP")]
    [InlineData("POST", "api/v1/inventory/abc/run", "OfficeManager", "ProductionWorker", "CAP-PLAN-ABC")]
    [InlineData("POST", "api/v1/inventory/abc/runs/{runId:int}/apply", "Manager", "PM", "CAP-PLAN-ABC")]
    [InlineData("PUT", "api/v1/lots/{id:int}", "ProductionWorker", "OfficeManager", "CAP-INV-LOTS")]
    [InlineData("POST", "api/v1/serials/{id:int}/transfer", "Engineer", "ProductionWorker", "CAP-INV-SERIALS")]
    [InlineData("POST", "api/v1/kanban-cards", "Manager", "Engineer", "CAP-EXT-KANBAN-REPLENISHMENT")]
    [InlineData("POST", "api/v1/kanban-cards/{id:int}/trigger", "ProductionWorker", "PM", "CAP-EXT-KANBAN-REPLENISHMENT")]
    [InlineData("POST", "api/v1/purchase-orders/{id:int}/receive", "Procurement", "ProductionWorker", "CAP-P2P-PO")]
    public void Inventory_screen_endpoints_admit_their_roles_and_carry_their_capability(
        string verb, string template, string admitted, string refused, string capability)
    {
        var action = Action(verb, template);
        var roles = EffectiveRoles(action);

        roles.Should().Contain(admitted, $"{verb} {template}");
        roles.Should().NotContain(refused, $"{verb} {template}");
        Capabilities(action).Should().Contain(capability, $"{verb} {template}");
    }
}
