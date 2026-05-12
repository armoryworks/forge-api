using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Forge.Core.Models;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Workflows;

/// <summary>
/// Workflow Pattern Phase 3 — End-to-end integration tests for the entity
/// readiness validators API. Reuses <see cref="CapabilityTestWebApplicationFactory"/>
/// per the standing finding "don't spawn another factory."
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class EntityValidatorsApiTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task List_ReturnsSeededPartValidators()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/entity-validators?entityType=Part");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<EntityValidatorResponseModel>>();
        rows.Should().NotBeNull();
        rows!.Select(r => r.ValidatorId).Should().Contain(["hasBasics", "hasBom", "hasRouting", "hasCost"]);
        rows!.All(r => r.IsSeedData).Should().BeTrue();
    }

    [Fact]
    public async Task Create_RejectsNonAdmin()
    {
        var client = AuthenticatedClient(role: "Engineer");
        var body = new UpsertEntityValidatorRequestModel(
            "Part", "tempCustom",
            """{"type":"fieldPresent","field":"description"}""",
            "validators.parts.tempCustom",
            "validators.parts.tempCustomMissing");
        var response = await client.PostAsJsonAsync("/api/v1/entity-validators", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteSeededRow_Forbidden_409()
    {
        var client = AuthenticatedClient();
        var list = await client.GetFromJsonAsync<List<EntityValidatorResponseModel>>(
            "/api/v1/entity-validators?entityType=Part");
        var seed = list!.First(r => r.ValidatorId == "hasBom");

        var response = await client.DeleteAsync($"/api/v1/entity-validators/{seed.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
