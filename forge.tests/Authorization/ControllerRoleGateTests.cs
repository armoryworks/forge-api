using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Forge.Tests.Authorization;

/// <summary>
/// Endpoints that carried only a bare <c>[Authorize]</c> (any signed-in user) although the
/// screen that calls them is limited to a few roles. Each case pins the denied role and an
/// admitted one, so a later edit can neither reopen the gap nor shut out the real users.
/// Effective roles are the AND of the controller's and the action's <c>[Authorize(Roles)]</c>.
/// </summary>
public sealed class ControllerRoleGateTests
{
    private static readonly string[] AllRoles =
    [
        "Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager",
        "Controller", "Procurement",
    ];

    private static IEnumerable<(MethodInfo Action, string Template)> Endpoints(string verb) =>
        from controller in typeof(Program).Assembly.GetTypes()
        where controller is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(controller)
        let prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        from http in action.GetCustomAttributes<HttpMethodAttribute>()
        where http.HttpMethods.Contains(verb)
        let template = http.Template is null ? prefix : prefix is null ? http.Template : $"{prefix}/{http.Template}"
        select (action, template ?? string.Empty);

    internal static HashSet<string> EffectiveRoles(string verb, string template)
    {
        var matches = Endpoints(verb).Where(e => e.Template == template).ToList();
        matches.Should().ContainSingle($"{verb} {template} must resolve to exactly one action");
        var action = matches[0].Action;

        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        var attributes = action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>());
        foreach (var attribute in attributes.Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    [Fact]
    public void Track_type_custom_fields_are_edited_by_admin_only()
    {
        EffectiveRoles("PUT", "api/v1/track-types/{id:int}/custom-fields").Should().BeEquivalentTo(["Admin"]);
        EffectiveRoles("GET", "api/v1/track-types/{id:int}/custom-fields").Should().Contain("ProductionWorker");
    }

    [Theory]
    [InlineData("POST", "api/v1/sequences/definitions")]
    [InlineData("PUT", "api/v1/sequences/definitions/{id:int}")]
    [InlineData("POST", "api/v1/sequences/definitions/{id:int}/publish")]
    [InlineData("POST", "api/v1/sequences/definitions/{id:int}/new-version")]
    [InlineData("DELETE", "api/v1/sequences/definitions/{id:int}")]
    [InlineData("POST", "api/v1/sequences/resource-clocks")]
    [InlineData("DELETE", "api/v1/sequences/resource-clocks/{id:int}")]
    public void Sequence_design_is_admin_and_manager_only(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("GET", "api/v1/sequences/definitions")]
    [InlineData("POST", "api/v1/sequences/instances/{id:int}/steps/{stepKey}/complete")]
    public void Sequence_runs_stay_open_to_the_shop_floor(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().Contain("ProductionWorker", $"{verb} {template}");
    }

    [Theory]
    [InlineData("GET", "api/v1/mobile/stock/on-hand")]
    [InlineData("POST", "api/v1/mobile/stock/move")]
    [InlineData("GET", "api/v1/scanner/context/{partIdentifier}")]
    [InlineData("POST", "api/v1/scanner/move")]
    [InlineData("POST", "api/v1/scanner/count")]
    [InlineData("POST", "api/v1/scanner/receive")]
    [InlineData("POST", "api/v1/scanner/issue")]
    [InlineData("POST", "api/v1/scanner/reverse")]
    public void Scanner_and_mobile_stock_actions_match_the_inventory_roles(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(
            ["Admin", "Manager", "OfficeManager", "Engineer", "ProductionWorker"], $"{verb} {template}");
        Forge.Api.Authorization.InventoryRoles.Edit
            .Should().Be("Admin,Manager,OfficeManager,Engineer,ProductionWorker", "the scanner mirrors the inventory controller's writers");
    }

    [Theory]
    [InlineData("api/v1/reports/copq")]
    [InlineData("api/v1/reports/copq/trend")]
    [InlineData("api/v1/reports/copq/pareto")]
    public void Cost_of_poor_quality_is_a_reporting_role_read(string template)
    {
        EffectiveRoles("GET", template).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "PM"]);
    }

    [Fact]
    public void A_production_worker_cannot_delete_a_part()
    {
        EffectiveRoles("DELETE", "api/v1/parts/{id:int}").Should().BeEquivalentTo(["Admin", "Manager", "Engineer"]);
        EffectiveRoles("GET", "api/v1/parts/{id:int}").Should().Contain("ProductionWorker");
    }

    [Theory]
    [InlineData("POST", "api/v1/planning-cycles")]
    [InlineData("PUT", "api/v1/planning-cycles/{id:int}")]
    [InlineData("POST", "api/v1/planning-cycles/{id:int}/activate")]
    [InlineData("POST", "api/v1/planning-cycles/{id:int}/complete")]
    [InlineData("POST", "api/v1/planning-cycles/{id:int}/entries")]
    [InlineData("DELETE", "api/v1/planning-cycles/{id:int}/entries/{jobId:int}")]
    [InlineData("PUT", "api/v1/planning-cycles/{id:int}/entries/order")]
    [InlineData("POST", "api/v1/planning-cycles/{id:int}/entries/{jobId:int}/complete")]
    public void Planning_cycles_are_run_by_managers_and_project_managers(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager", "PM"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("api/v1/time-tracking/overtime/{userId:int}")]
    [InlineData("api/v1/time-tracking/corrections")]
    public void Payroll_roles_read_overtime_and_time_corrections(string template)
    {
        EffectiveRoles("GET", template).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Controller"]);
    }

    [Theory]
    [InlineData("PATCH", "api/v1/time-tracking/entries/{id:int}/correct")]
    [InlineData("POST", "api/v1/time-tracking/lock-period")]
    [InlineData("POST", "api/v1/leave/requests/{id:int}/approve")]
    [InlineData("POST", "api/v1/leave/requests/{id:int}/deny")]
    public void Time_and_leave_decisions_stay_with_admin_and_manager(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("PUT", "api/v1/leave/policies/{id:int}")]
    [InlineData("POST", "api/v1/leave/balances/{userId:int}/adjust")]
    [InlineData("GET", "api/v1/time-tracking/pay-period/preview")]
    public void Leave_policies_balance_adjustments_and_pay_period_setup_are_admin_only(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin"], $"{verb} {template}");
    }

    [Fact]
    public void Payroll_roles_read_every_leave_balance_and_everyone_reads_and_cancels_their_own()
    {
        EffectiveRoles("GET", "api/v1/leave/balances").Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Controller"]);
        EffectiveRoles("GET", "api/v1/leave/balances/{userId:int}/adjustments").Should().Contain("ProductionWorker");
        EffectiveRoles("POST", "api/v1/leave/requests/{id:int}/cancel").Should().Contain("ProductionWorker");
    }

    [Theory]
    [InlineData("POST", "api/v1/parts/{id:int}/operations")]
    [InlineData("PATCH", "api/v1/parts/{id:int}/operations/{operationId:int}")]
    [InlineData("PUT", "api/v1/parts/{id:int}/operations/order")]
    [InlineData("DELETE", "api/v1/parts/{id:int}/operations/{operationId:int}")]
    [InlineData("POST", "api/v1/parts/{id:int}/operations/{operationId:int}/materials")]
    [InlineData("DELETE", "api/v1/parts/{id:int}/operations/{operationId:int}/materials/{materialId:int}")]
    public void Routing_operations_are_edited_by_engineering_roles_only(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager", "Engineer"], $"{verb} {template}");
        EffectiveRoles("GET", "api/v1/parts/{id:int}/operations").Should().Contain(["PM", "ProductionWorker"]);
    }

    [Fact]
    public void Only_the_commercial_roles_delete_a_customer()
    {
        EffectiveRoles("DELETE", "api/v1/customers/{id:int}").Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"]);
        EffectiveRoles("GET", "api/v1/customers/{id:int}").Should().Contain(["PM", "Engineer"]);
    }

    [Theory]
    [InlineData("GET", "api/v1/pricing/resolve")]
    [InlineData("POST", "api/v1/cpq/configure")]
    [InlineData("POST", "api/v1/cpq/validate")]
    [InlineData("GET", "api/v1/cpq/configurators")]
    [InlineData("GET", "api/v1/cpq/configurators/{id:int}")]
    [InlineData("POST", "api/v1/cpq/configurations")]
    [InlineData("GET", "api/v1/cpq/configurations/{id:int}")]
    [InlineData("POST", "api/v1/cpq/configurations/{id:int}/generate-quote")]
    [InlineData("POST", "api/v1/cpq/configurations/{id:int}/generate-part")]
    public void Customer_prices_resolve_for_the_price_list_roles_only(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager"], $"{verb} {template}");
        EffectiveRoles(verb, template).Should().BeEquivalentTo(EffectiveRoles("GET", "api/v1/price-lists"));
    }

    [Theory]
    [InlineData("POST", "api/v1/cpq/configurators")]
    [InlineData("PUT", "api/v1/cpq/configurators/{id:int}")]
    public void Configurator_design_stays_with_admin_and_manager(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("GET", "api/v1/replenishment/burn-rates")]
    [InlineData("GET", "api/v1/replenishment/suggestions")]
    [InlineData("POST", "api/v1/replenishment/suggestions/{id:int}/approve")]
    [InlineData("POST", "api/v1/replenishment/suggestions/approve-bulk")]
    [InlineData("POST", "api/v1/replenishment/suggestions/{id:int}/dismiss")]
    [InlineData("GET", "api/v1/replenishment/settings")]
    public void Procurement_works_reorder_suggestions(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(["Admin", "Manager", "Procurement"], $"{verb} {template}");
    }

    [Fact]
    public void Replenishment_settings_are_changed_by_admin_and_manager()
    {
        EffectiveRoles("PUT", "api/v1/replenishment/settings").Should().BeEquivalentTo(["Admin", "Manager"]);
    }

    [Theory]
    [InlineData("POST", "api/v1/jobs/{jobId:int}/operations/{opId:int}/send-out")]
    [InlineData("GET", "api/v1/jobs/{jobId:int}/subcontract-orders")]
    [InlineData("GET", "api/v1/jobs/{jobId:int}/subcontract-operations")]
    [InlineData("GET", "api/v1/shop-floor/pending-subcontracts")]
    [InlineData("GET", "api/v1/reports/subcontract-spending")]
    public void Procurement_reads_subcontract_orders_and_raises_their_purchase_orders(string verb, string template)
    {
        EffectiveRoles(verb, template).Should().BeEquivalentTo(
            ["Admin", "Manager", "Engineer", "PM", "Procurement"], $"{verb} {template}");
    }

    [Fact]
    public void Receiving_a_subcontract_back_stays_with_the_production_roles()
    {
        EffectiveRoles("POST", "api/v1/subcontract-orders/{id:int}/receive")
            .Should().BeEquivalentTo(["Admin", "Manager", "Engineer", "PM"]);
    }

    [Fact]
    public void Engineers_look_up_vendors_without_reading_the_vendor_master()
    {
        EffectiveRoles("GET", "api/v1/vendors/lookup").Should().BeEquivalentTo(
            ["Admin", "Manager", "OfficeManager", "Procurement", "Engineer"]);
        EffectiveRoles("GET", "api/v1/vendors").Should().NotContain("Engineer");
        EffectiveRoles("POST", "api/v1/vendors").Should().NotContain("Engineer");
        EffectiveRoles("POST", "api/v1/vendor-parts").Should().Contain("Engineer");
    }
}
