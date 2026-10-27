using System.Reflection;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Behaviors;
using Forge.Api.Capabilities;
using Forge.Api.Controllers;
using Forge.Api.Features.VendorReturns;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Authorization;

/// <summary>
/// The roles and capabilities of the vendor credit memo, return-to-vendor and payment-run endpoints, resolved the way
/// ASP.NET Core combines controller and action <c>[Authorize(Roles)]</c> (AND) and the capability gate reads the most
/// specific attribute.
/// </summary>
public sealed class VendorMemoRoleTests
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
    [InlineData("POST", "api/v1/vendor-credit-memos")]
    [InlineData("PUT", "api/v1/vendor-credit-memos/{id:int}")]
    [InlineData("DELETE", "api/v1/vendor-credit-memos/{id:int}")]
    [InlineData("POST", "api/v1/vendor-credit-memos/{id:int}/issue")]
    [InlineData("POST", "api/v1/vendor-credit-memos/{id:int}/applications")]
    [InlineData("DELETE", "api/v1/vendor-credit-memos/{id:int}/applications/{applicationId:int}")]
    public void Memo_writes_follow_the_vendor_bill_roles(string verb, string template)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);
        Roles(action).Should().NotContain(["Procurement", "Controller", "ProductionWorker"]);
        Capability(action).Should().Be("CAP-P2P-BILL");
    }

    [Fact]
    public void Voiding_a_memo_is_for_admins_and_managers()
        => Roles(Action("POST", "api/v1/vendor-credit-memos/{id:int}/void")).Should().BeEquivalentTo(["Admin", "Manager"]);

    [Theory]
    [InlineData("api/v1/vendor-credit-memos")]
    [InlineData("api/v1/vendor-credit-memos/{id:int}")]
    [InlineData("api/v1/vendor-credit-memos/{id:int}/pdf")]
    [InlineData("api/v1/vendor-credit-memos/statement/{vendorId:int}")]
    public void Memo_reads_admit_procurement_and_the_controller(string template)
    {
        var action = Action("GET", template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Procurement", "Controller"]);
        Roles(action).Should().NotContain("PM");
        Capability(action).Should().Be("CAP-P2P-BILL");
    }

    [Theory]
    [InlineData("GET", "api/v1/vendor-returns")]
    [InlineData("GET", "api/v1/vendor-returns/{id:int}")]
    [InlineData("GET", "api/v1/vendor-returns/{id:int}/pdf")]
    [InlineData("POST", "api/v1/vendor-returns/{id:int}/ship")]
    [InlineData("POST", "api/v1/vendor-returns/{id:int}/credit")]
    public void Returns_to_vendor_follow_the_receiving_roles(string verb, string template)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Procurement"]);
        Roles(action).Should().NotContain(["Controller", "Engineer"]);
        Capability(action).Should().Be("CAP-P2P-PO");
        action.DeclaringType!.GetCustomAttribute<RequiresAnyCapabilityAttribute>()!.Capabilities.Should().Equal("CAP-QC-NCR");
    }

    [Theory]
    [InlineData("POST", "api/v1/payment-runs")]
    [InlineData("POST", "api/v1/payment-runs/{id:int}/resume")]
    [InlineData("POST", "api/v1/payment-runs/{id:int}/cancel")]
    public void Paying_a_run_follows_the_vendor_payment_roles(string verb, string template)
    {
        var action = Action(verb, template);
        Roles(action).Should().BeEquivalentTo(Roles(Action("POST", "api/v1/vendor-payments")));
        Roles(action).Should().NotContain("Controller");
        Capability(action).Should().Be("CAP-P2P-PAY");
    }

    [Fact]
    public void The_controller_reads_payment_runs()
    {
        Roles(Action("GET", "api/v1/payment-runs")).Should().Contain("Controller").And.NotContain("Procurement");
        Roles(Action("GET", "api/v1/payment-runs/{id:int}")).Should().Contain("Controller");
    }

    [Fact]
    public async Task Recording_a_return_credit_is_refused_while_vendor_bills_are_off()
    {
        var behavior = new CapabilityGateBehavior<RecordVendorReturnCreditCommand, VendorCreditMemoDetailResponseModel>(
            new StubCapabilitySnapshotProvider("CAP-P2P-PO", "CAP-QC-NCR"));

        var act = () => behavior.Handle(
            new RecordVendorReturnCreditCommand(1, new RecordVendorReturnCreditRequestModel()),
            _ => Task.FromResult<VendorCreditMemoDetailResponseModel>(null!),
            CancellationToken.None);

        await act.Should().ThrowAsync<CapabilityDisabledException>();
    }
}
