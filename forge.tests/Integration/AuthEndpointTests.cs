using System.Net;
using System.Net.Http.Json;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class AuthEndpointTests
{
    private readonly HttpClient _client;

    public AuthEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_DoesNotReturn200()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "invalid@test.com",
            Password = "wrongpassword"
        });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetupStatus_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/auth/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SsoProviders_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/auth/sso/providers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
