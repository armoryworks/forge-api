using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class PasskeyWireFormatTests
{
    private readonly InMemorySessionStore _sessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PasskeyWireFormatTests(TestWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ISessionStore>(_sessions)));
    }

    [Fact]
    public async Task RegistrationOptions_UseTheWebAuthnWireNames()
    {
        var client = await SignedInClientAsync("passkey-wire@forge.test");

        var response = await client.PostAsync("/api/v1/auth/passkeys/register/options", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        var param = options.GetProperty("pubKeyCredParams")[0];
        param.GetProperty("type").GetString().Should().Be("public-key");
        param.GetProperty("alg").ValueKind.Should().Be(JsonValueKind.Number);
        options.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString().Should().Be("required");
        options.GetProperty("attestation").GetString().Should().Be("none");
    }

    [Fact]
    public async Task AssertionResponse_WithWebAuthnEnumValues_IsAcceptedByTheBinder()
    {
        var anon = _factory.CreateClient();
        var mfaPendingToken = _factory.Services.GetRequiredService<IMfaPreAuthTokenService>().Issue(987654);

        var response = await anon.PostAsJsonAsync("/api/v1/auth/passkeys/challenge/validate", new
        {
            mfaPendingToken,
            response = new
            {
                id = "AQID",
                rawId = "AQID",
                type = "public-key",
                response = new { authenticatorData = "AQID", clientDataJSON = "AQID", signature = "AQID" },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the body binds and reaches the assertion check, which has no pending challenge for this user");
    }

    [Fact]
    public async Task MalformedAssertionResponse_IsABadRequest()
    {
        var anon = _factory.CreateClient();

        var response = await anon.PostAsJsonAsync("/api/v1/auth/passkeys/challenge/validate", new
        {
            mfaPendingToken = "x",
            response = new { type = "not-a-credential-type" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> SignedInClientAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Pass",
            LastName = "Key",
            IsActive = true,
        };
        (await users.CreateAsync(user, "Test1234!Aa")).Succeeded.Should().BeTrue();

        var issued = scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateToken(user.Id, email, user.FirstName, user.LastName, null, null, []);
        await _sessions.CreateSessionAsync(user.Id, issued.Jti, issued.ExpiresAt);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        return client;
    }
}
