using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// Pins who may call the production-run, machine-hours, downtime and prediction-run endpoints the run and
/// maintenance screens use, and which capability each answers to.
/// </summary>
public sealed class ProductionMaintenanceRoleGateTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager", "Controller", "Procurement",
    ];

    private static MethodInfo Action(Type controller, string verb, string template)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.GetCustomAttributes<HttpMethodAttribute>()
                .Any(h => h.HttpMethods.Contains(verb) && $"{prefix}/{h.Template}" == template));
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

    private static string Capability(MethodInfo action) =>
        (action.GetCustomAttribute<RequiresCapabilityAttribute>()
            ?? action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>())!.Capability;

    [Theory]
    [InlineData("GET", "api/v1/jobs/{id:int}/production-runs")]
    [InlineData("POST", "api/v1/jobs/{id:int}/production-runs")]
    [InlineData("PUT", "api/v1/jobs/{id:int}/production-runs/{runId:int}")]
    [InlineData("POST", "api/v1/jobs/{id:int}/production-runs/{runId:int}/receive-to-stock")]
    public void Production_runs_are_open_to_the_job_roles_under_mfg_complete(string verb, string template)
    {
        var action = Action(typeof(JobsController), verb, template);
        Roles(action).Should().Contain(["ProductionWorker", "Manager"]).And.NotContain(["Controller", "Procurement"]);
        Capability(action).Should().Be("CAP-MFG-COMPLETE");
    }

    [Fact]
    public void Recording_hours_is_admin_manager_engineer_under_preventive_maintenance()
    {
        var action = Action(typeof(AssetsController), "PATCH", "api/v1/assets/{id:int}/hours");
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        Capability(action).Should().Be("CAP-MAINT-PM");
    }

    [Theory]
    [InlineData("POST", "api/v1/assets/{id:int}/downtime")]
    [InlineData("PATCH", "api/v1/assets/downtime/{id:int}/end")]
    public void Downtime_is_admin_manager_engineer_under_breakdown(string verb, string template)
    {
        var action = Action(typeof(AssetsController), verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        Capability(action).Should().Be("CAP-MAINT-BREAKDOWN");
    }

    [Fact]
    public void Running_predictions_for_every_work_center_is_admin_and_manager_under_predictive()
    {
        var action = Action(typeof(PredictiveMaintenanceController), "POST", "api/v1/predictions/run");
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager"]);
        Roles(action).Should().NotContain("Engineer");
        Capability(action).Should().Be("CAP-MAINT-PREDICTIVE");
    }
}
