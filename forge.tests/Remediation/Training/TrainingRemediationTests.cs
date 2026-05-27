using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Training;

/// <summary>
/// Region 5 · Training RED test (see ../README.md). Finding F-14-BE-01: the training-path
/// write API is absent — the admin dialog POSTs to /training/paths but the API is GET/seed-only
/// (paths can't be created/edited/deleted). Endpoints sit behind CAP-HR-TRAINING (default OFF),
/// so the test enables the cap first, then asserts the create-path route exists (today absent → 404).
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

    [Fact(Skip = "RED: F-14-BE-01 — training paths are GET/seed-only; the admin dialog's POST has no " +
                 "backend. Remove Skip when POST /api/v1/training/paths exists (create-path handler).")]
    public async Task Training_path_create_endpoint_exists()
    {
        var admin = AuthClient();
        await admin.PutAsync("/api/v1/capabilities/CAP-HR-TRAINING/enabled",
            JsonContent.Create(new { enabled = true }));

        var body = JsonContent.Create(new { name = "Onboarding Path", description = "x", moduleIds = Array.Empty<int>() });
        var response = await admin.PostAsync("/api/v1/training/paths", body);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "admins must be able to create training paths, not just consume seeded ones");
    }
}
