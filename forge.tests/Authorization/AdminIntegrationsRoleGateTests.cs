using System.Reflection;

using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Authorization;

/// <summary>
/// The admin integration surfaces (webhooks, cloud storage, AI indexing, machine connections,
/// report schedules) pin both the admitted roles and a refused one, and each sits behind its
/// capability so turning the capability off closes the endpoints.
/// </summary>
public sealed class AdminIntegrationsRoleGateTests
{
    private static HashSet<string> Roles(string verb, string template) => ControllerRoleGateTests.EffectiveRoles(verb, template);

    [Theory]
    [InlineData("GET", "api/v1/admin/webhooks")]
    [InlineData("GET", "api/v1/admin/webhooks/event-types")]
    [InlineData("POST", "api/v1/admin/webhooks")]
    [InlineData("PUT", "api/v1/admin/webhooks/{subscriptionId:int}")]
    [InlineData("DELETE", "api/v1/admin/webhooks/{subscriptionId:int}")]
    [InlineData("POST", "api/v1/admin/webhooks/{subscriptionId:int}/test")]
    [InlineData("GET", "api/v1/admin/webhooks/{subscriptionId:int}/deliveries")]
    [InlineData("POST", "api/v1/admin/webhooks/deliveries/{deliveryId:int}/redeliver")]
    [InlineData("GET", "api/v1/admin/cloud-storage/provider-types")]
    [InlineData("GET", "api/v1/admin/cloud-storage/providers")]
    [InlineData("POST", "api/v1/admin/cloud-storage/providers")]
    [InlineData("PUT", "api/v1/admin/cloud-storage/providers/{id:int}")]
    [InlineData("DELETE", "api/v1/admin/cloud-storage/providers/{id:int}")]
    [InlineData("POST", "api/v1/admin/cloud-storage/providers/{id:int}/test")]
    [InlineData("GET", "api/v1/admin/cloud-storage/folder-map")]
    [InlineData("PUT", "api/v1/admin/cloud-storage/folder-map")]
    [InlineData("GET", "api/v1/ai/index/status")]
    [InlineData("POST", "api/v1/ai/bulk-index")]
    [InlineData("GET", "api/v1/scheduled-tasks")]
    [InlineData("POST", "api/v1/scheduled-tasks")]
    public void Integration_admin_endpoints_are_admin_only(string verb, string template)
    {
        Roles(verb, template).Should().BeEquivalentTo(["Admin"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("GET", "api/v1/admin/machine-connections")]
    [InlineData("POST", "api/v1/admin/machine-connections")]
    [InlineData("PUT", "api/v1/admin/machine-connections/{id:int}")]
    [InlineData("POST", "api/v1/admin/machine-connections/{id:int}/test")]
    [InlineData("GET", "api/v1/report-builder/schedules")]
    [InlineData("POST", "api/v1/report-builder/schedules")]
    [InlineData("POST", "api/v1/report-builder/schedules/{id:int}/run")]
    [InlineData("DELETE", "api/v1/report-builder/schedules/{id:int}")]
    public void Machine_connections_and_report_schedules_admit_admin_and_manager_only(string verb, string template)
    {
        Roles(verb, template).Should().BeEquivalentTo(["Admin", "Manager"], $"{verb} {template}");
    }

    [Theory]
    [InlineData("POST", "api/v1/ai/help/stream")]
    public void The_help_stream_stays_open_to_every_signed_in_role(string verb, string template)
    {
        Roles(verb, template).Should().Contain(["ProductionWorker", "PM", "Engineer"]);
    }

    [Theory]
    [InlineData(typeof(WebhooksController), "CAP-CROSS-WEBHOOKS")]
    [InlineData(typeof(CloudStorageController), "CAP-EXT-CLOUD-STORAGE")]
    [InlineData(typeof(AiController), "CAP-EXT-AI-ASSISTANT")]
    [InlineData(typeof(MachineConnectionsController), "CAP-MFG-MACHINE-CONNECT")]
    public void Each_surface_is_closed_when_its_capability_is_off(Type controller, string capability)
    {
        controller.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability.Should().Be(capability);
    }
}
