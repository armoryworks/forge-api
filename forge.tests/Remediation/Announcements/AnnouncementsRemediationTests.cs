using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Announcements;

/// <summary>
/// Region 5 · Announcements RED tests (see ../README.md). Finding F-13-ANN-01: announcements
/// were create-only (no update/retract). Now GREEN — PUT /announcements/{id} (edit) +
/// DELETE /announcements/{id} (retract/soft-delete), both [Authorize(Roles="Admin,Manager")]
/// behind CAP-EXT-ANNOUNCEMENTS (default OFF, so each test enables it first).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class AnnouncementsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public AnnouncementsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task EnableAnnouncements(HttpClient admin) =>
        await admin.PutAsync("/api/v1/capabilities/CAP-EXT-ANNOUNCEMENTS/enabled", JsonContent.Create(new { enabled = true }));

    private async Task<int> SeedAnnouncement()
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var announcement = new Announcement { Title = "Original", Content = "Original content", CreatedById = 1 };
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync();
        return announcement.Id;
    }

    [Fact] // F-13-ANN-01 GREEN — a published announcement can be edited
    public async Task Announcement_can_be_edited()
    {
        var admin = AuthClient();
        await EnableAnnouncements(admin);
        var id = await SeedAnnouncement();

        var body = JsonContent.Create(new
        {
            title = "Corrected title",
            content = "Corrected content",
            severity = "Info",
            requiresAcknowledgment = false,
            expiresAt = (DateTimeOffset?)null,
        });
        var response = await admin.PutAsync($"/api/v1/announcements/{id}", body);

        response.IsSuccessStatusCode.Should().BeTrue("a published announcement must be editable");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Corrected title", "the edit must persist");
    }

    [Fact] // F-13-ANN-01 GREEN — a published announcement can be retracted
    public async Task Announcement_can_be_retracted()
    {
        var admin = AuthClient();
        await EnableAnnouncements(admin);
        var id = await SeedAnnouncement();

        var response = await admin.DeleteAsync($"/api/v1/announcements/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "a published announcement must be retractable");
    }
}
