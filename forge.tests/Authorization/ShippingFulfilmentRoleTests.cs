using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// The pick-wave screens, the shipment packages/pickup/address controls and the EDI send buttons are shown
/// only to the roles these endpoints admit, behind the capability the controller carries. These cases pin both
/// so the screens and the API cannot drift apart.
/// </summary>
public sealed class ShippingFulfilmentRoleTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager", "Controller", "Procurement",
    ];

    private static HashSet<string> EffectiveRoles(Type controller, string verb, string template)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        var action = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(h =>
                h.HttpMethods.Contains(verb) && (h.Template is null ? prefix : $"{prefix}/{h.Template}") == template));

        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        foreach (var attribute in controller.GetCustomAttributes<AuthorizeAttribute>()
                     .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                     .Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    [Theory]
    [InlineData("GET", "api/v1/pick-waves")]
    [InlineData("GET", "api/v1/pick-waves/my-lines")]
    [InlineData("GET", "api/v1/pick-waves/{id:int}")]
    [InlineData("POST", "api/v1/pick-waves/auto-generate")]
    [InlineData("POST", "api/v1/pick-waves/{id:int}/release")]
    [InlineData("POST", "api/v1/pick-waves/{id:int}/lines/{lineId:int}/confirm")]
    [InlineData("POST", "api/v1/pick-waves/{id:int}/complete")]
    [InlineData("GET", "api/v1/pick-waves/{id:int}/pdf")]
    public void Pick_waves_are_worked_by_the_warehouse_roles_and_not_by_project_managers(string verb, string template)
    {
        var roles = EffectiveRoles(typeof(PickWavesController), verb, template);
        roles.Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Engineer", "ProductionWorker"], $"{verb} {template}");
        roles.Should().NotContain("PM");
    }

    [Theory]
    [InlineData("GET", "api/v1/shipments/{id:int}/packages")]
    [InlineData("POST", "api/v1/shipments/{id:int}/packages")]
    [InlineData("PATCH", "api/v1/shipments/{id:int}/packages/{packageId:int}")]
    [InlineData("DELETE", "api/v1/shipments/{id:int}/packages/{packageId:int}")]
    [InlineData("GET", "api/v1/shipments/pickup-origin")]
    [InlineData("POST", "api/v1/shipments/{id:int}/pickup")]
    [InlineData("POST", "api/v1/shipments/{id:int}/pickup/cancel")]
    [InlineData("POST", "api/v1/shipments/validate-address")]
    [InlineData("GET", "api/v1/shipments/{id:int}/packing-slip")]
    [InlineData("GET", "api/v1/shipments/{id:int}/bill-of-lading")]
    public void Shipment_controls_follow_the_shipping_roles(string verb, string template)
    {
        var roles = EffectiveRoles(typeof(ShipmentsController), verb, template);
        roles.Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"], $"{verb} {template}");
        roles.Should().NotContain(["ProductionWorker", "PM"]);
    }

    [Theory]
    [InlineData("POST", "api/v1/edi/send/{entityType}/{entityId:int}")]
    [InlineData("GET", "api/v1/edi/trading-partners")]
    [InlineData("GET", "api/v1/edi/transactions")]
    [InlineData("PUT", "api/v1/edi/trading-partners/{id:int}")]
    public void Edi_sends_are_admin_and_manager_only(string verb, string template)
    {
        EffectiveRoles(typeof(EdiController), verb, template).Should().BeEquivalentTo(["Admin", "Manager"], $"{verb} {template}");
    }

    [Theory]
    [InlineData(typeof(PickWavesController), "CAP-O2C-PICKPACK")]
    [InlineData(typeof(ShipmentsController), "CAP-O2C-SHIP")]
    [InlineData(typeof(EdiController), "CAP-CROSS-INTEG-EDI")]
    public void Each_surface_is_behind_its_capability(Type controller, string capability)
    {
        controller.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability.Should().Be(capability);
    }
}
