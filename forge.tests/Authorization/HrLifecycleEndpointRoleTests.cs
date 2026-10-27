using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;

namespace Forge.Tests.Authorization;

/// <summary>
/// Role and capability gates on the termination, performance-review and training-enrolment
/// endpoints, resolved the way ASP.NET Core combines controller and action attributes (AND for
/// roles, the closest capability attribute wins).
/// </summary>
public sealed class HrLifecycleEndpointRoleTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "IT Admin", "Procurement", "Production Manager", "Production Planner",
        "ComplianceOfficer",
    ];

    private static IEnumerable<(MethodInfo Action, string Verb, string Template)> Endpoints() =>
        from controller in typeof(Program).Assembly.GetTypes()
        where controller is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(controller)
        let prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        from http in action.GetCustomAttributes<HttpMethodAttribute>()
        from verb in http.HttpMethods
        let template = http.Template is null ? prefix : prefix is null ? http.Template : $"{prefix}/{http.Template}"
        select (action, verb, template ?? string.Empty);

    private static MethodInfo Single(string verb, string template)
    {
        var matches = Endpoints().Where(e => e.Verb == verb && e.Template == template).ToList();
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

    private static string? Capability(MethodInfo action)
        => (action.GetCustomAttribute<RequiresCapabilityAttribute>()
            ?? action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>())?.Capability;

    [Theory]
    [InlineData("POST", "api/v1/employees/{id:int}/terminate")]
    [InlineData("POST", "api/v1/employees/{id:int}/rehire")]
    [InlineData("GET", "api/v1/employees/{id:int}/final-pay-summary")]
    [InlineData("POST", "api/v1/employees/{id:int}/final-pay-processed")]
    public void Termination_endpoints_are_admin_and_manager_behind_the_termination_capability(string verb, string template)
    {
        var action = Single(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(["Admin", "Manager"]);
        Capability(action).Should().Be("CAP-HR-TERMINATION");
    }

    [Theory]
    [InlineData("POST", "api/v1/reviews/cycles", "Admin")]
    [InlineData("PUT", "api/v1/reviews/cycles/{id:int}", "Admin")]
    [InlineData("POST", "api/v1/reviews/cycles/{id:int}/launch", "Admin")]
    [InlineData("POST", "api/v1/reviews/cycles/{id:int}/close", "Admin")]
    [InlineData("GET", "api/v1/reviews/cycles/{id:int}", "Admin,Manager")]
    [InlineData("GET", "api/v1/reviews/team", "Admin,Manager")]
    [InlineData("POST", "api/v1/reviews/{id:int}/manager-submit", "Admin,Manager")]
    [InlineData("POST", "api/v1/reviews/{id:int}/close", "Admin,Manager")]
    public void Review_administration_is_limited(string verb, string template, string roles)
    {
        var action = Single(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(roles.Split(','));
        EffectiveRoles(action).Should().NotContain("ProductionWorker");
        Capability(action).Should().Be("CAP-HR-REVIEW");
    }

    [Theory]
    [InlineData("GET", "api/v1/reviews/mine")]
    [InlineData("GET", "api/v1/reviews/{id:int}")]
    [InlineData("POST", "api/v1/reviews/{id:int}/self-submit")]
    [InlineData("POST", "api/v1/reviews/{id:int}/acknowledge")]
    public void Employees_reach_their_own_review_actions(string verb, string template)
    {
        var action = Single(verb, template);

        EffectiveRoles(action).Should().Contain(["ProductionWorker", "Engineer", "Admin"]);
        Capability(action).Should().Be("CAP-HR-REVIEW");
    }

    [Theory]
    [InlineData("POST", "api/v1/training/paths/{id:int}/enrollments", "Admin")]
    [InlineData("PATCH", "api/v1/training/enrollments/{id:int}", "Admin")]
    [InlineData("DELETE", "api/v1/training/enrollments/{id:int}", "Admin")]
    [InlineData("GET", "api/v1/training/paths/{id:int}/enrollments", "Admin,Manager")]
    public void Training_enrolment_writes_are_admin_and_progress_reads_admin_and_manager(string verb, string template, string roles)
    {
        var action = Single(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(roles.Split(','));
    }
}
