using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Authorization;
using Forge.Api.Capabilities;

namespace Forge.Tests.Authorization;

public sealed class KioskFloorEndpointRoleTests
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
        var attributes = action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>());
        foreach (var attribute in attributes.Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    private static string? EffectiveCapability(MethodInfo action)
        => (action.GetCustomAttribute<RequiresCapabilityAttribute>()
            ?? action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>())?.Capability;

    [Theory]
    [InlineData("GET", "api/v1/spc/jobs/{jobId:int}/characteristics")]
    [InlineData("POST", "api/v1/spc/measurements")]
    public void A_floor_worker_reads_a_jobs_characteristics_and_records_measurements(string verb, string template)
    {
        var action = Action(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(AllRoles, $"{verb} {template}");
        EffectiveCapability(action).Should().Be("CAP-QC-SPC");
    }

    [Theory]
    [InlineData("POST", "api/v1/spc/characteristics")]
    [InlineData("PUT", "api/v1/spc/characteristics/{id:int}")]
    public void Characteristic_authoring_stays_with_admin_and_manager(string verb, string template)
    {
        var roles = EffectiveRoles(Action(verb, template));

        roles.Should().BeEquivalentTo(["Admin", "Manager"], $"{verb} {template}");
        roles.Should().NotContain("ProductionWorker");
    }

    [Fact]
    public void Issue_sources_follow_the_scanner_stock_roles()
    {
        var action = Action("GET", "api/v1/scanner/issue-sources/{partId:int}");
        var roles = EffectiveRoles(action);

        roles.Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Engineer", "ProductionWorker"]);
        roles.Should().NotContain(["PM", "Controller", "Procurement"]);
        EffectiveCapability(action).Should().Be("CAP-MFG-SHOPFLOOR");
    }

    [Theory]
    [InlineData("GET", "api/v1/shop-floor/andon/alerts")]
    [InlineData("POST", "api/v1/shop-floor/andon/alerts/{id:int}/acknowledge")]
    [InlineData("POST", "api/v1/shop-floor/andon/alerts/{id:int}/resolve")]
    public void Andon_board_actions_are_open_to_every_signed_in_role_with_andon_on(string verb, string template)
    {
        var action = Action(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(AllRoles, $"{verb} {template}");
        EffectiveCapability(action).Should().Be("CAP-EXT-ANDON");
    }

    [Fact]
    public void The_kiosk_andon_board_reads_with_a_terminal_token_and_needs_andon_on()
    {
        var action = Action("GET", "api/v1/display/shop-floor/andon/alerts");

        action.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        action.GetCustomAttribute<KioskTerminalAuthAttribute>().Should().NotBeNull();
        EffectiveCapability(action).Should().Be("CAP-EXT-ANDON");
    }
}
