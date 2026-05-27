using System.Net;

using FluentAssertions;

using Forge.Tests.Capabilities;

namespace Forge.Tests.Remediation.Capabilities;

/// <summary>
/// Region 5 · Capabilities RED test (see ../README.md). Finding F-13-CAP-04: the capability
/// relations endpoint (GET /capabilities/{id}/relations) is reachable by any authenticated
/// user (it's [CapabilityBootstrap], not role-gated), exposing the install's capability
/// topology. This asserts a low-priv role is rejected (403). Today a ProductionWorker reads
/// it (200).
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CapabilityRelationsRemediationTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;
    public CapabilityRelationsRemediationTests(CapabilityTestWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "5");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact(Skip = "RED: F-13-CAP-04 — GET /capabilities/{id}/relations is readable by any authed user. " +
                 "Remove Skip when it requires Admin (403 for a ProductionWorker).")]
    public async Task Capability_relations_are_admin_only()
    {
        var response = await AuthClient("ProductionWorker")
            .GetAsync("/api/v1/capabilities/CAP-MD-CUSTOMERS/relations");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the capability topology is admin configuration — not readable by every authenticated user");
    }
}
