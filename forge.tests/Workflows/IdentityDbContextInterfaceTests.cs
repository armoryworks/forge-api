using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Data.Context;
using Forge.Identity.Interfaces;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Workflows;

/// <summary>
/// Phase C — verifies the per-vertical DbContext interface unblocker.
/// <see cref="IIdentityDbContext"/> must resolve to the single
/// <see cref="AppDbContext"/> scope so handlers that depend on the interface
/// share the same unit of work as everything else in the request.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class IdentityDbContextInterfaceTests(CapabilityTestWebApplicationFactory factory)
{
    [Fact]
    public void IIdentityDbContext_ResolvesToTheSameAppDbContextScope()
    {
        using var scope = factory.Services.CreateScope();
        var iface = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();
        var concrete = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        iface.Should().BeSameAs(concrete,
            "the interface registration must delegate to the single AppDbContext " +
            "scope — otherwise a handler on the interface would have a separate " +
            "change tracker from the rest of the request");
    }

    [Fact]
    public async Task IIdentityDbContext_ExposesIdentityDbSets()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        // Smoke: the Identity DbSets are reachable + queryable through the
        // segregated interface (proves the surface is wired, not just declared).
        (await db.UserScanDevices.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await db.UserPreferences.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await db.UserMfaDevices.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
        (await db.Users.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
    }
}
