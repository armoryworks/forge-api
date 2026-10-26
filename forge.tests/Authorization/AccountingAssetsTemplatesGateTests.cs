using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// The fixed-asset, journal-template and overhead-pool endpoints sit on the Controller-only accounting surface:
/// the Controller role is admitted and every other role (Admin and Manager included) is refused, and each
/// endpoint carries the capability that switches it off (writes need full GL or depreciation; reads follow the
/// ledger view).
/// </summary>
public sealed class AccountingAssetsTemplatesGateTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "Procurement",
    ];

    private static MethodInfo Action(string verb, string template)
    {
        var prefix = typeof(AccountingGlController).GetCustomAttribute<RouteAttribute>()!.Template;
        var matches = typeof(AccountingGlController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>()
                .Any(h => h.HttpMethods.Contains(verb) && $"{prefix}/{h.Template}" == template))
            .ToList();
        matches.Should().ContainSingle($"{verb} {template} must resolve to exactly one action");
        return matches[0];
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

    private static string Capability(MethodInfo action)
        => action.GetCustomAttribute<RequiresCapabilityAttribute>()?.Capability
           ?? action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability;

    [Theory]
    [InlineData("GET", "api/v1/accounting/fixed-assets/{id:int}", "CAP-ACCT-DEPRECIATION")]
    [InlineData("PUT", "api/v1/accounting/fixed-assets/{id:int}", "CAP-ACCT-DEPRECIATION")]
    [InlineData("GET", "api/v1/accounting/fixed-assets/linkable-assets", "CAP-ACCT-DEPRECIATION")]
    [InlineData("POST", "api/v1/accounting/depreciation/preview", "CAP-ACCT-DEPRECIATION")]
    [InlineData("POST", "api/v1/accounting/depreciation/run", "CAP-ACCT-DEPRECIATION")]
    [InlineData("POST", "api/v1/accounting/fixed-assets/{id:int}/dispose", "CAP-ACCT-DEPRECIATION")]
    [InlineData("POST", "api/v1/accounting/fixed-assets/{id:int}/dispose/preview", "CAP-ACCT-DEPRECIATION")]
    [InlineData("GET", "api/v1/accounting/journal-templates/{id:int}", "CAP-ACCT-GL-VIEW")]
    [InlineData("GET", "api/v1/accounting/journal-templates/parties", "CAP-ACCT-GL-VIEW")]
    [InlineData("PUT", "api/v1/accounting/journal-templates/{id:int}", "CAP-ACCT-FULLGL")]
    [InlineData("DELETE", "api/v1/accounting/journal-templates/{id:int}", "CAP-ACCT-FULLGL")]
    [InlineData("GET", "api/v1/accounting/overhead/status", "CAP-ACCT-GL-VIEW")]
    [InlineData("POST", "api/v1/accounting/overhead/record", "CAP-ACCT-FULLGL")]
    [InlineData("POST", "api/v1/accounting/overhead/close", "CAP-ACCT-FULLGL")]
    public void Endpoint_is_controller_only_and_capability_gated(string verb, string template, string capability)
    {
        var action = Action(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(["Controller"], $"{verb} {template}");
        EffectiveRoles(action).Should().NotContain(["Admin", "Manager"]);
        Capability(action).Should().Be(capability, $"{verb} {template}");
    }

    [Fact]
    public void Depreciation_endpoints_are_off_whenever_full_gl_is_off()
    {
        CapabilityCatalogRelations.Dependencies
            .Should().Contain(d => d.From == "CAP-ACCT-DEPRECIATION" && d.To == "CAP-ACCT-FULLGL");
    }
}
