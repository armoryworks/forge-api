using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Middleware;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class MfaEnrollmentGateTests
{
    private const string Password = "Test1234!Aa";
    private const string ProbeUrl = "/api/v1/users";

    private readonly InMemorySessionStore _sessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public MfaEnrollmentGateTests(TestWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ISessionStore>(_sessions)));
    }

    [Fact]
    public async Task UnenrolledUserInAnMfaRole_CanOnlyReachSignInAndEnrolment_UntilEnrolled()
    {
        var role = $"MfaProbe{Guid.NewGuid():N}"[..20];
        var (userId, email) = await SeedAdminAsync(role);
        await SetRequiredRolesAsync([role]);
        try
        {
            var login = await LoginAsync(email);
            login.GetProperty("mfaEnrollmentRequired").GetBoolean().Should().BeTrue();
            var client = ClientWith(login.GetProperty("token").GetString()!);

            var refused = await client.GetAsync(ProbeUrl);
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("code").GetString().Should().Be(MfaEnrollmentGateMiddleware.ErrorCode);

            (await client.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/v1/auth/mfa/status")).StatusCode.Should().Be(HttpStatusCode.OK);

            var stillGated = await client.PostAsync("/api/v1/auth/refresh", null);
            stillGated.StatusCode.Should().Be(HttpStatusCode.OK);
            var stillGatedBody = await stillGated.Content.ReadFromJsonAsync<JsonElement>();
            stillGatedBody.GetProperty("mfaEnrollmentRequired").GetBoolean().Should().BeTrue();
            client = ClientWith(stillGatedBody.GetProperty("token").GetString()!);
            (await client.GetAsync(ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            await MarkEnrolledAsync(userId);
            var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);
            refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
            var refreshedBody = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
            refreshedBody.GetProperty("mfaEnrollmentRequired").GetBoolean().Should().BeFalse();

            (await ClientWith(refreshedBody.GetProperty("token").GetString()!).GetAsync(ProbeUrl))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await ClearRequiredRolesAsync();
        }
    }

    [Fact]
    public async Task UserOutsideTheMfaRoles_IsNotGated()
    {
        var (_, email) = await SeedAdminAsync($"MfaProbe{Guid.NewGuid():N}"[..20]);
        await SetRequiredRolesAsync(["Controller"]);
        try
        {
            var login = await LoginAsync(email);
            login.GetProperty("mfaEnrollmentRequired").GetBoolean().Should().BeFalse();

            (await ClientWith(login.GetProperty("token").GetString()!).GetAsync(ProbeUrl))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await ClearRequiredRolesAsync();
        }
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<JsonElement> LoginAsync(string email)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(int UserId, string Email)> SeedAdminAsync(string extraRole)
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        foreach (var role in new[] { "Admin", extraRole })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<int>(role));
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{extraRole.ToLowerInvariant()}@forge.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Mfa",
            LastName = "Probe",
            IsActive = true,
        };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        await users.AddToRolesAsync(user, ["Admin", extraRole]);
        return (user.Id, email);
    }

    private async Task SetRequiredRolesAsync(IEnumerable<string> roles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await MfaRolePolicy.SetRequiredRolesAsync(db, roles, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private async Task ClearRequiredRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SystemSettings.RemoveRange(await db.SystemSettings.Where(s => s.Key == MfaRolePolicy.SettingKey).ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task MarkEnrolledAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.MfaEnabled = true;
        await db.SaveChangesAsync();
    }
}
