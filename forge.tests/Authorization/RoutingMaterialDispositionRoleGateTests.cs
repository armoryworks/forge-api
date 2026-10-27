using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;

namespace Forge.Tests.Authorization;

/// <summary>
/// Routing writes are engineering master data: production workers read routings but never change them, in the
/// list or the flowchart. Material specs are managed by the engineering roles, and the write-off account list for
/// an Other disposition is open to the roles that dispose jobs, only while the GL is on.
/// </summary>
public sealed class RoutingMaterialDispositionRoleGateTests
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

    private static HashSet<string> EffectiveRoles(string verb, string template)
    {
        var action = Action(verb, template);
        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        foreach (var attribute in action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
                     .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                     .Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    private static IEnumerable<string> Capabilities(string verb, string template)
    {
        var action = Action(verb, template);
        return action.GetCustomAttributes<RequiresCapabilityAttribute>()
            .Concat(action.DeclaringType!.GetCustomAttributes<RequiresCapabilityAttribute>())
            .Select(a => a.Capability);
    }

    [Theory]
    [InlineData("POST", "api/v1/parts/{id:int}/operations")]
    [InlineData("PATCH", "api/v1/parts/{id:int}/operations/{operationId:int}")]
    [InlineData("PUT", "api/v1/parts/{id:int}/operations/order")]
    [InlineData("PUT", "api/v1/parts/{id:int}/routing/graph")]
    [InlineData("DELETE", "api/v1/parts/{id:int}/operations/{operationId:int}")]
    [InlineData("PUT", "api/v1/parts/{id:int}/step-gating")]
    [InlineData("POST", "api/v1/parts/{id:int}/routing/copy-from/{sourcePartId:int}")]
    public void Routing_writes_admit_the_engineering_roles_and_refuse_production_workers(string verb, string template)
    {
        var roles = EffectiveRoles(verb, template);

        roles.Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        roles.Should().NotContain("ProductionWorker");
    }

    [Theory]
    [InlineData("GET", "api/v1/parts/{id:int}/operations")]
    [InlineData("GET", "api/v1/parts/{id:int}/routing/graph")]
    public void Production_workers_still_read_the_routing(string verb, string template)
        => EffectiveRoles(verb, template).Should().Contain("ProductionWorker");

    [Theory]
    [InlineData("GET", "api/v1/material-specs")]
    [InlineData("POST", "api/v1/material-specs")]
    [InlineData("PUT", "api/v1/material-specs/{id:int}")]
    [InlineData("POST", "api/v1/material-specs/{id:int}/retire")]
    [InlineData("POST", "api/v1/material-specs/{id:int}/reactivate")]
    public void Material_specs_are_managed_by_the_engineering_roles_under_the_parts_capability(string verb, string template)
    {
        var roles = EffectiveRoles(verb, template);

        roles.Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        roles.Should().NotContain(["PM", "ProductionWorker", "OfficeManager"]);
        Capabilities(verb, template).Should().Contain("CAP-MD-PARTS");
    }

    [Fact]
    public void The_disposition_write_off_accounts_follow_the_dispose_roles_and_need_the_gl()
    {
        var roles = EffectiveRoles("GET", "api/v1/jobs/disposition-expense-accounts");

        roles.Should().BeEquivalentTo(EffectiveRoles("POST", "api/v1/jobs/{id:int}/dispose"));
        roles.Should().NotContain(["ProductionWorker", "Engineer"]);
        Capabilities("GET", "api/v1/jobs/disposition-expense-accounts").Should().Contain("CAP-ACCT-FULLGL");
    }
}
