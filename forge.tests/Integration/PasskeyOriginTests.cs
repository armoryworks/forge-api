using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class PasskeyOriginTests
{
    private readonly InMemorySessionStore _sessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PasskeyOriginTests(TestWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ISessionStore>(_sessions)));
    }

    [Fact]
    public async Task Registration_behind_a_proxy_that_drops_the_port_accepts_the_browser_origin()
    {
        var (client, userId) = await SignedInClientAsync("passkey-port@forge.test", "http://localhost:4510");

        var options = await RegisterOptionsAsync(client);
        options.GetProperty("rp").GetProperty("id").GetString().Should().Be("localhost");

        var response = await client.PostAsJsonAsync("/api/v1/auth/passkeys/register",
            AttestationBody(options, "http://localhost:4510"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await PasskeyCountAsync(userId)).Should().Be(1);
    }

    [Fact]
    public async Task Registration_from_a_foreign_origin_is_refused()
    {
        var (client, userId) = await SignedInClientAsync("passkey-foreign@forge.test", "http://evil.example:4510");

        var options = await RegisterOptionsAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/auth/passkeys/register",
            AttestationBody(options, "http://evil.example:4510"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PasskeyCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task Client_data_from_another_port_than_the_browser_origin_is_refused()
    {
        var (client, userId) = await SignedInClientAsync("passkey-otherport@forge.test", "http://localhost:4510");

        var options = await RegisterOptionsAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/auth/passkeys/register",
            AttestationBody(options, "http://localhost:9999"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PasskeyCountAsync(userId)).Should().Be(0);
    }

    private static async Task<JsonElement> RegisterOptionsAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/auth/passkeys/register/options", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object AttestationBody(JsonElement options, string clientOrigin)
    {
        var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
        var challenge = options.GetProperty("challenge").GetString()!;

        var clientData = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["type"] = "webauthn.create",
            ["challenge"] = challenge,
            ["origin"] = clientOrigin,
            ["crossOrigin"] = false,
        });

        var credentialId = RandomNumberGenerator.GetBytes(16);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);

        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);
        cose.WriteInt32(3); cose.WriteInt32(-7);
        cose.WriteInt32(-1); cose.WriteInt32(1);
        cose.WriteInt32(-2); cose.WriteByteString(p.Q.X!);
        cose.WriteInt32(-3); cose.WriteByteString(p.Q.Y!);
        cose.WriteEndMap();

        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.Add(0x45);
        authData.AddRange(new byte[4]);
        authData.AddRange(new byte[16]);
        authData.Add(0);
        authData.Add((byte)credentialId.Length);
        authData.AddRange(credentialId);
        authData.AddRange(cose.Encode());

        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(authData.ToArray());
        attestation.WriteEndMap();

        var id = Base64Url(credentialId);
        return new
        {
            response = new
            {
                id,
                rawId = id,
                type = "public-key",
                response = new
                {
                    attestationObject = Base64Url(attestation.Encode()),
                    clientDataJSON = Base64Url(clientData),
                },
                clientExtensionResults = new { },
            },
            deviceName = "Test key",
        };
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<int> PasskeyCountAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserMfaDevices.CountAsync(
            d => d.UserId == userId && d.DeviceType == MfaDeviceType.WebAuthn);
    }

    private async Task<(HttpClient client, int userId)> SignedInClientAsync(string email, string browserOrigin)
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
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        client.DefaultRequestHeaders.Add("Origin", browserOrigin);
        return (client, user.Id);
    }
}
