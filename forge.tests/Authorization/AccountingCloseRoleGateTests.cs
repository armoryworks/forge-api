using System.Reflection;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using FluentAssertions;
using MediatR;

using Forge.Api.Behaviors;
using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Tests.Authorization;

/// <summary>
/// Role and capability gates of the period-close, bank, conversion and payroll endpoints: the books are
/// Controller-only (Admin, Manager and the rest are refused), the company time zone is Admin-only, and each
/// endpoint carries the capability its screen is shown under. Effective roles are the AND of the controller's
/// and the action's <c>[Authorize(Roles)]</c>; the effective capability is the action's attribute, else the
/// controller's.
/// </summary>
public sealed class AccountingCloseRoleGateTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "Procurement",
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

    private static string? EffectiveCapability(MethodInfo action)
        => action.GetCustomAttribute<RequiresCapabilityAttribute>()?.Capability
            ?? action.DeclaringType!.GetCustomAttribute<RequiresCapabilityAttribute>()?.Capability;

    [Theory]
    [InlineData("GET", "api/v1/accounting/bank-statements/lines/{lineId:long}/candidates", "CAP-ACCT-FULLGL")]
    [InlineData("POST", "api/v1/accounting/bank-statements/lines/{lineId:long}/match/{journalLineId:long}", "CAP-ACCT-FULLGL")]
    [InlineData("GET", "api/v1/accounting/books/{id:int}/settings", "CAP-ACCT-GL-VIEW")]
    [InlineData("PUT", "api/v1/accounting/books/{id:int}/settings", "CAP-ACCT-FULLGL")]
    [InlineData("POST", "api/v1/accounting/opening-balances/validate", "CAP-ACCT-FULLGL")]
    [InlineData("POST", "api/v1/accounting/opening-balances", "CAP-ACCT-FULLGL")]
    [InlineData("POST", "api/v1/accounting/payroll/runs/import/preview", "CAP-ACCT-FULLGL")]
    [InlineData("POST", "api/v1/accounting/payroll/runs/import", "CAP-ACCT-FULLGL")]
    [InlineData("GET", "api/v1/accounting/pay-runs", "CAP-PAYROLL-RUN")]
    [InlineData("GET", "api/v1/accounting/pay-runs/{id:int}/journal-preview", "CAP-PAYROLL-RUN")]
    [InlineData("POST", "api/v1/accounting/pay-runs/{id:int}/post", "CAP-PAYROLL-RUN")]
    [InlineData("POST", "api/v1/accounting/qbo-export/push", "CAP-ACCT-QBO-EXPORT")]
    public void Books_endpoints_are_controller_only_and_capability_gated(string verb, string template, string capability)
    {
        var action = Action(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(["Controller"], $"{verb} {template}");
        EffectiveCapability(action).Should().Be(capability, $"{verb} {template}");
    }

    [Theory]
    [InlineData("GET", "api/v1/admin/company-profile")]
    [InlineData("PATCH", "api/v1/admin/company-profile")]
    [InlineData("POST", "api/v1/admin/company-profile/apply-time-zone-to-books")]
    public void Company_time_zone_is_admin_only(string verb, string template)
    {
        var action = Action(verb, template);

        EffectiveRoles(action).Should().BeEquivalentTo(["Admin"], $"{verb} {template}");
        action.GetCustomAttribute<CapabilityBootstrapAttribute>().Should().NotBeNull("company settings stay reachable with any capability set");
    }
    private sealed class Snapshot(params string[] enabled) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            enabled.ToDictionary(c => c, _ => true, StringComparer.Ordinal), DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task AssertRefusedWithoutAsync<TRequest, TResponse>(TRequest request, string capability)
        where TRequest : IRequest<TResponse>
    {
        var off = new CapabilityGateBehavior<TRequest, TResponse>(new Snapshot());
        var ex = await Assert.ThrowsAsync<CapabilityDisabledException>(
            () => off.Handle(request, _ => Task.FromResult(default(TResponse)!), CancellationToken.None));
        ex.Capability.Should().Be(capability);

        var on = new CapabilityGateBehavior<TRequest, TResponse>(new Snapshot(capability));
        await on.Invoking(b => b.Handle(request, _ => Task.FromResult(default(TResponse)!), CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task New_requests_are_refused_while_their_capability_is_off()
    {
        await AssertRefusedWithoutAsync<UpdateBookSettingsCommand, BookSettingsModel>(
            new UpdateBookSettingsCommand(1, 10m, null), "CAP-ACCT-FULLGL");
        await AssertRefusedWithoutAsync<GetBookSettingsQuery, BookSettingsModel>(
            new GetBookSettingsQuery(1), "CAP-ACCT-GL-VIEW");
        await AssertRefusedWithoutAsync<ValidateOpeningOpenItemsQuery, OpeningOpenItemsValidationModel>(
            new ValidateOpeningOpenItemsQuery(new ValidateOpeningOpenItemsRequestModel(1, [])), "CAP-ACCT-FULLGL");
        await AssertRefusedWithoutAsync<GetPayRunJournalPreviewQuery, IReadOnlyList<PayRunJournalPreviewLineModel>>(
            new GetPayRunJournalPreviewQuery(1), "CAP-PAYROLL-RUN");
    }
}
