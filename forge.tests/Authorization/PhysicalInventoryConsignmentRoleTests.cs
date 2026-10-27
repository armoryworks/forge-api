using System.Net;
using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

using Forge.Api.Controllers;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Authorization;

/// <summary>
/// Physical inventory: counting is open to the scanner's stock roles, while creating, freezing, review, variance,
/// posting and cancelling are Admin and Manager. Consignment agreements keep Admin, Manager, OfficeManager and
/// Engineer on every action, including the transaction history. The physical inventory endpoints answer 403 while
/// CAP-INV-PHYSICAL is off (its default).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public sealed class PhysicalInventoryConsignmentRoleTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly string[] AllRoles =
        ["Admin", "Manager", "Engineer", "PM", "ProductionWorker", "OfficeManager", "Controller", "Procurement"];

    private static HashSet<string> EffectiveRoles(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"{controller.Name}.{actionName} not found");
        action.GetCustomAttributes<HttpMethodAttribute>().Should().NotBeEmpty();
        var allowed = new HashSet<string>(AllRoles, StringComparer.Ordinal);
        foreach (var attribute in controller.GetCustomAttributes<AuthorizeAttribute>()
                     .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
                     .Where(a => !string.IsNullOrEmpty(a.Roles)))
            allowed.IntersectWith(attribute.Roles!.Split(',').Select(r => r.Trim()));
        return allowed;
    }

    [Theory]
    [InlineData(nameof(PhysicalInventoriesController.List))]
    [InlineData(nameof(PhysicalInventoriesController.Get))]
    [InlineData(nameof(PhysicalInventoriesController.GetTag))]
    [InlineData(nameof(PhysicalInventoriesController.CountSheets))]
    [InlineData(nameof(PhysicalInventoriesController.AddTag))]
    [InlineData(nameof(PhysicalInventoriesController.EnterCount))]
    public void Counting_is_open_to_the_stock_roles(string action)
    {
        EffectiveRoles(typeof(PhysicalInventoriesController), action)
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Engineer", "ProductionWorker"], action);
    }

    [Theory]
    [InlineData(nameof(PhysicalInventoriesController.Create))]
    [InlineData(nameof(PhysicalInventoriesController.Freeze))]
    [InlineData(nameof(PhysicalInventoriesController.SubmitForReview))]
    [InlineData(nameof(PhysicalInventoriesController.Reopen))]
    [InlineData(nameof(PhysicalInventoriesController.Variance))]
    [InlineData(nameof(PhysicalInventoriesController.Post))]
    [InlineData(nameof(PhysicalInventoriesController.Cancel))]
    public void Managing_the_count_is_admin_and_manager_only(string action)
    {
        EffectiveRoles(typeof(PhysicalInventoriesController), action).Should().BeEquivalentTo(["Admin", "Manager"], action);
    }

    [Theory]
    [InlineData(nameof(ConsignmentAgreementsController.RecordReceipt))]
    [InlineData(nameof(ConsignmentAgreementsController.RecordConsumption))]
    [InlineData(nameof(ConsignmentAgreementsController.Reconcile))]
    [InlineData(nameof(ConsignmentAgreementsController.GetTransactions))]
    [InlineData(nameof(ConsignmentAgreementsController.GetStockSummary))]
    public void Consignment_keeps_its_roles(string action)
    {
        EffectiveRoles(typeof(ConsignmentAgreementsController), action)
            .Should().BeEquivalentTo(["Admin", "Manager", "OfficeManager", "Engineer"], action);
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("PM", HttpStatusCode.Forbidden)]
    public async Task Physical_inventory_is_refused_while_the_capability_is_off(string role, HttpStatusCode expected)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);

        var response = await client.GetAsync("/api/v1/inventory/physical-inventory");

        response.StatusCode.Should().Be(expected);
        if (role == "Admin")
            response.Headers.GetValues("X-Capability-Disabled").Should().Contain("CAP-INV-PHYSICAL");
    }

    [Fact]
    public async Task A_production_worker_is_refused_the_consignment_agreements()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "ProductionWorker");

        var response = await client.GetAsync("/api/v1/consignment-agreements/1/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
