using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Training;

/// <summary>
/// Region 5 · Training RED tests (see ../README.md). Finding F-14-BE-01: training paths
/// were GET/seed-only (the admin dialog's POST had no backend). Now GREEN — admin
/// create / update / delete paths. Endpoints sit behind CAP-HR-TRAINING (default OFF) +
/// require the Admin role, so each test enables the cap first as an Admin client.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class TrainingRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public TrainingRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task EnableTraining(HttpClient admin) =>
        await admin.PutAsync("/api/v1/capabilities/CAP-HR-TRAINING/enabled", JsonContent.Create(new { enabled = true }));

    [Fact] // F-14-BE-01 GREEN — an admin can create a training path
    public async Task Admin_can_create_a_training_path()
    {
        var admin = AuthClient();
        await EnableTraining(admin);

        var body = JsonContent.Create(new
        {
            title = "Onboarding Path",
            slug = $"onboarding-{Guid.NewGuid():N}",
            description = "New-hire essentials",
            icon = "school",
            isAutoAssigned = false,
            isActive = true,
            sortOrder = 1,
            allowedRoles = (string?)null,
            moduleIds = Array.Empty<int>(),
        });
        var response = await admin.PostAsync("/api/v1/training/paths", body);

        response.IsSuccessStatusCode.Should().BeTrue("admins must be able to create training paths");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Onboarding Path", "the created path must round-trip");
    }

    [Fact] // F-14-BE-01 GREEN — an admin can delete a training path
    public async Task Admin_can_delete_a_training_path()
    {
        var admin = AuthClient();
        await EnableTraining(admin);

        int pathId;
        using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var path = new TrainingPath { Title = "Disposable Path", Slug = $"disposable-{Guid.NewGuid():N}" };
            db.TrainingPaths.Add(path);
            await db.SaveChangesAsync();
            pathId = path.Id;
        }

        var response = await admin.DeleteAsync($"/api/v1/training/paths/{pathId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "admins must be able to retire a training path");
    }
}
