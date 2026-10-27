using System.Net;
using System.Net.Http.Json;
using System.Reflection;

using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Features.Chat;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Handlers.Admin;

[Collection(CapabilityTestCollection.Name)]
public class AdminOrgNotifyEndpointRoleTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static bool CapabilityBlocked(HttpResponseMessage response) =>
        response.Headers.Contains("X-Capability-Disabled");

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Engineer")]
    [InlineData("OfficeManager")]
    public async Task Only_admins_and_managers_open_the_scanner_registry(string role)
    {
        var client = Client(role);

        (await client.GetAsync("/api/v1/scanner/devices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/api/v1/scanner/devices/999", new { deviceName = "x", assignedUserId = (int?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync("/api/v1/scanner/devices/999")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task Admins_and_managers_use_the_scanner_registry(string role)
    {
        var client = Client(role);

        (await client.GetAsync("/api/v1/scanner/devices")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/api/v1/scanner/devices/999", new { deviceName = "x", assignedUserId = (int?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_engineer_cannot_create_shifts()
    {
        var response = await Client("Engineer").PostAsJsonAsync("/api/v1/shifts", new { name = "Day", startTime = "07:00:00", endTime = "15:00:00", breakMinutes = 30 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        CapabilityBlocked(response).Should().BeFalse();
    }

    [Fact]
    public async Task A_manager_passes_the_shift_role_check_and_meets_the_capability_gate_while_shifts_are_off()
    {
        var response = await Client("Manager").PostAsJsonAsync("/api/v1/shifts", new { name = "Day", startTime = "07:00:00", endTime = "15:00:00", breakMinutes = 30 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        CapabilityBlocked(response).Should().BeTrue();
    }

    [Fact]
    public async Task Only_admins_import_translations()
    {
        var body = new { languageCode = "es", entries = Array.Empty<object>() };

        (await Client("Manager").PostAsJsonAsync("/api/v1/i18n/overrides/import", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = await Client("Admin").PostAsJsonAsync("/api/v1/i18n/overrides/import", body);
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Direct_message_uploads_are_gated_by_chat()
    {
        using var content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.txt" } };

        var response = await Client("Admin").PostAsync("/api/v1/chat/messages/2/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        CapabilityBlocked(response).Should().BeTrue();
    }

    [Fact]
    public void Direct_message_uploads_also_need_attachments()
    {
        typeof(UploadDirectMessageFileCommand).GetCustomAttribute<RequiresCapabilityAttribute>()!
            .Capability.Should().Be("CAP-CROSS-ATTACHMENTS");
    }
}
