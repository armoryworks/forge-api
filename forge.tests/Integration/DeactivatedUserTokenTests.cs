using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class DeactivatedUserTokenTests
{
    private const string ProbeUrl = "/api/v1/users";

    private readonly InMemorySessionStore _sessions = new();
    private readonly WebApplicationFactory<Program> _factory;

    public DeactivatedUserTokenTests(TestWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ISessionStore>(_sessions)));
    }

    [Fact]
    public async Task IssuedToken_StopsWorking_OnceTheUserIsInactive()
    {
        var (userId, token) = await SeedUserWithSessionAsync("stale-pm@forge.test", "PM");
        var client = ClientWith(token);

        (await client.GetAsync(ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            user!.IsActive = false;
            await users.UpdateAsync(user);
        }

        (await client.GetAsync(ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminDeactivation_RejectsTheUsersExistingToken_AndRevokesTheirSessions()
    {
        var (_, adminToken) = await SeedUserWithSessionAsync("deactivating-admin@forge.test", "Admin");
        var (pmId, pmToken) = await SeedUserWithSessionAsync("deactivated-pm@forge.test", "PM");
        var admin = ClientWith(adminToken);
        var pm = ClientWith(pmToken);

        (await pm.GetAsync(ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await admin.PutAsJsonAsync($"/api/v1/admin/users/{pmId}", new { id = pmId, isActive = false });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        _sessions.HasLiveSession(pmId).Should().BeFalse();
        (await pm.GetAsync(ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await pm.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync(ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(int UserId, string Token)> SeedUserWithSessionAsync(string email, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        if (!await roles.RoleExistsAsync(role))
            await roles.CreateAsync(new IdentityRole<int>(role));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = role,
            IsActive = true,
        };
        (await users.CreateAsync(user, "Test1234!Aa")).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, role);

        var issued = scope.ServiceProvider.GetRequiredService<ITokenService>()
            .GenerateToken(user.Id, email, user.FirstName, user.LastName, null, null, [role]);
        await _sessions.CreateSessionAsync(user.Id, issued.Jti, issued.ExpiresAt);
        return (user.Id, issued.Token);
    }
}
