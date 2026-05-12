using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-A contract tests for <c>GET /api/v1/capabilities/descriptor</c>.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class CapabilityDescriptorTests
{
    private readonly CapabilityTestWebApplicationFactory _factory;

    public CapabilityDescriptorTests(CapabilityTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "admin");
        return client;
    }

    [Fact]
    public async Task GET_Descriptor_Without_Auth_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/capabilities/descriptor");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Descriptor_With_Auth_Returns_200_With_Expected_Shape()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("/api/v1/capabilities/descriptor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DescriptorBody>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Capabilities);
        Assert.NotEmpty(body.Capabilities!);
        Assert.True(body.TotalCount > 0);
        Assert.True(body.EnabledCount > 0);

        // Shape spot-check: every entry has the documented fields.
        var first = body.Capabilities![0];
        Assert.False(string.IsNullOrWhiteSpace(first.Code));
        Assert.False(string.IsNullOrWhiteSpace(first.Area));
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
    }

    [Fact]
    public async Task Descriptor_Includes_CAP_MD_CUSTOMERS_Sanity_Check_Seeder_Ran()
    {
        var client = AuthenticatedClient();
        var body = await client.GetFromJsonAsync<DescriptorBody>("/api/v1/capabilities/descriptor");
        Assert.NotNull(body);

        var customer = body!.Capabilities!.FirstOrDefault(c => c.Code == "CAP-MD-CUSTOMERS");
        Assert.NotNull(customer);
        Assert.True(customer!.IsDefaultOn);
        Assert.True(customer.Enabled, "CAP-MD-CUSTOMERS is default-on, so a fresh-install seed must enable it.");
    }

    [Fact]
    public async Task Descriptor_Includes_Capability_Admin_Catalog_Amendment()
    {
        var client = AuthenticatedClient();
        var body = await client.GetFromJsonAsync<DescriptorBody>("/api/v1/capabilities/descriptor");
        Assert.NotNull(body);

        var admin = body!.Capabilities!.FirstOrDefault(c => c.Code == "CAP-IDEN-CAPABILITY-ADMIN");
        Assert.NotNull(admin);
        Assert.True(admin!.IsDefaultOn, "Capability-admin must be default-on per 4E-decisions-log #9.");
        Assert.True(admin.Enabled, "Capability-admin should be enabled on a fresh install.");
        Assert.Equal("Admin", admin.RequiresRoles);
    }

    /// <summary>
    /// Phase A's enabled-count assertion. Per 4F implementation-decisions.md:
    /// the catalog markdown header claims 41 default-on, but the actual catalog
    /// body has 54 distinct default-on entries. Plus CAP-IDEN-CAPABILITY-ADMIN
    /// (the amendment) = 55. We assert >= 42 (the prompt's lower bound) so the
    /// test stays robust as catalog edits happen.
    /// </summary>
    [Fact]
    public async Task Descriptor_Default_On_Count_At_Least_42()
    {
        var client = AuthenticatedClient();
        var body = await client.GetFromJsonAsync<DescriptorBody>("/api/v1/capabilities/descriptor");
        Assert.NotNull(body);

        Assert.True(body!.EnabledCount >= 42,
            $"Expected at least 42 enabled capabilities (41 catalog + 1 admin amendment), found {body.EnabledCount}.");
    }

    private record DescriptorBody(
        [property: JsonPropertyName("totalCount")] int TotalCount,
        [property: JsonPropertyName("enabledCount")] int EnabledCount,
        [property: JsonPropertyName("capabilities")] List<DescriptorEntry>? Capabilities);

    private record DescriptorEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("area")] string Area,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("isDefaultOn")] bool IsDefaultOn,
        [property: JsonPropertyName("requiresRoles")] string? RequiresRoles);
}
