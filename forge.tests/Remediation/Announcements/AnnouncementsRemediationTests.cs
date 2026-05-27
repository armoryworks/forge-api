using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Announcements;

/// <summary>
/// Region 5 · Announcements RED test (see ../README.md). Finding F-13-ANN-01: announcements
/// are create-only — there is no update (PUT/PATCH) or retract path, so a published
/// announcement can't be corrected or pulled. Endpoints sit behind CAP-EXT-ANNOUNCEMENTS
/// (default OFF), so the test enables the cap first, then asserts the update route exists
/// (today absent → 404).
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

    [Fact(Skip = "RED: F-13-ANN-01 — announcements are create-only (no update/retract). " +
                 "Remove Skip when PUT /api/v1/announcements/{id} exists.")]
    public async Task Announcement_update_endpoint_exists()
    {
        var admin = AuthClient();
        await admin.PutAsync("/api/v1/capabilities/CAP-EXT-ANNOUNCEMENTS/enabled",
            JsonContent.Create(new { enabled = true }));

        var response = await admin.PutAsync("/api/v1/announcements/1",
            JsonContent.Create(new { title = "Corrected", body = "x" }));

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "a published announcement must be correctable / retractable");
    }
}
