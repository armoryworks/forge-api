using System.Reflection;

using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Authorization;

namespace Forge.Tests.Authorization;

/// <summary>
/// A job's routing steps are read by the roles that read the job, and worked (step timers, quantities, complete,
/// skip, reopen, reset) by the roles that move its stock, because completing the last step consumes material.
/// Both include the Production Manager, so the role that overrides a step gate can always reach the step.
/// Signed-in roles outside those lists are refused.
/// </summary>
public sealed class JobOperationRoleGateTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "IT Admin", "Procurement", "Production Manager", "Production Planner",
        "LeadIntake", "ComplianceOfficer",
    ];

    private static readonly string[] Workers =
        ["Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager", "Production Manager"];

    private static HashSet<string> EffectiveRoles(string verb, string template)
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

        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        foreach (var attribute in matches[0].DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
                     .Concat(matches[0].GetCustomAttributes<AuthorizeAttribute>())
                     .Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    [Theory]
    [InlineData("GET", "api/v1/job-operations/config")]
    [InlineData("GET", "api/v1/jobs/{jobId:int}/operations")]
    [InlineData("GET", "api/v1/jobs/{jobId:int}/operations/{operationId:int}/events")]
    public void Steps_are_read_by_the_job_readers_and_the_production_manager(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo([.. Workers, "Production Planner"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("POST", "api/v1/jobs/{jobId:int}/operations/{operationId:int}/timer/start")]
    [InlineData("POST", "api/v1/jobs/{jobId:int}/operations/{operationId:int}/timer/stop")]
    [InlineData("PATCH", "api/v1/jobs/{jobId:int}/operations/{operationId:int}")]
    public void Steps_are_worked_by_the_stock_moving_roles_and_the_production_manager(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(Workers, $"{verb} {template}");
    }

    [Fact]
    public void Every_role_that_overrides_a_step_gate_can_work_the_step()
    {
        StepGateRoles.Override.Split(',').Should().BeSubsetOf(JobRoles.OperationRecord.Split(','));
    }
}
