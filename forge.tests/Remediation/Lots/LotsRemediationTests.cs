using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Lots;

/// <summary>
/// Region 1 · Lots RED tests (see ../README.md). Finding L2: lots are create-only
/// (LotsController has GET/POST but no PUT/DELETE) so a mistaken lot can't be
/// corrected or archived despite the DeletedAt column. These assert the update +
/// soft-delete endpoints exist (today the routes are absent → 404/405).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class LotsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public LotsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: L2 — lots are create-only; no PUT to correct expiry/notes/supplier-lot. " +
                 "Remove Skip when PUT /api/v1/lots/{id} exists.")]
    public async Task Lot_update_endpoint_exists()
    {
        var response = await AuthClient().PutAsync("/api/v1/lots/1", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "a mistaken lot must be correctable, not frozen at creation");
    }

    [Fact(Skip = "RED: L2 — lots have no soft-delete path despite the DeletedAt column. " +
                 "Remove Skip when DELETE /api/v1/lots/{id} exists.")]
    public async Task Lot_delete_endpoint_exists()
    {
        var response = await AuthClient().DeleteAsync("/api/v1/lots/1");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "a mistaken lot must be archivable (soft delete)");
    }
}
