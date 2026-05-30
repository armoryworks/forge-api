using FluentAssertions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class ConnectionsRegistryTests
{
    private static (ConnectionsRegistry registry, AppDbContext db, Mock<ISystemSettingRepository> settings)
        Make()
    {
        var db = TestDbContextFactory.Create();
        var settings = new Mock<ISystemSettingRepository>();
        settings.Setup(s => s.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSetting?)null);
        return (new ConnectionsRegistry(db, settings.Object), db, settings);
    }

    private static async Task<ApplicationUser> SeedUserAsync(AppDbContext db, string email = "svc@example.local")
    {
        var user = new ApplicationUser
        {
            UserName = email, Email = email,
            FirstName = "Svc", LastName = "User", IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ListAsync_EmptyInstall_ReturnsEmpty()
    {
        var (registry, _, _) = Make();
        var rows = await registry.ListAsync(CancellationToken.None);
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_BiApiKey_AppearsActive_WithoutOwner()
    {
        var (registry, db, _) = Make();
        db.BiApiKeys.Add(new BiApiKey
        {
            Name = "Looker export",
            KeyHash = "h", KeyPrefix = "qbe_abc12345",
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        });
        await db.SaveChangesAsync();

        var rows = await registry.ListAsync(CancellationToken.None);

        rows.Should().ContainSingle(r => r.Kind == IntegrationKind.BiApiKey);
        var row = rows.Single(r => r.Kind == IntegrationKind.BiApiKey);
        row.Name.Should().Be("Looker export");
        row.OwnerEmail.Should().BeNull("BI keys are unbound — synthetic principal, no real user");
        row.Status.Should().Be("Active");
        row.ManageRoute.Should().Be("/admin/bi-api-keys");
    }

    [Fact]
    public async Task ListAsync_RevokedBiApiKey_Status_Revoked()
    {
        var (registry, db, _) = Make();
        db.BiApiKeys.Add(new BiApiKey
        {
            Name = "old", KeyHash = "h", KeyPrefix = "qbe_aa",
            IsActive = false,
        });
        await db.SaveChangesAsync();

        var rows = await registry.ListAsync(CancellationToken.None);
        rows.Single().Status.Should().Be("Revoked");
    }

    [Fact]
    public async Task ListAsync_ExpiredBiApiKey_Status_Expired()
    {
        var (registry, db, _) = Make();
        db.BiApiKeys.Add(new BiApiKey
        {
            Name = "stale", KeyHash = "h", KeyPrefix = "qbe_bb",
            IsActive = true, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var rows = await registry.ListAsync(CancellationToken.None);
        rows.Single().Status.Should().Be("Expired",
            "an IsActive=true row whose ExpiresAt is in the past is functionally expired " +
            "even though no admin has revoked it");
    }

    [Fact]
    public async Task ListAsync_SystemApiKey_CarriesBoundUserEmail()
    {
        var (registry, db, _) = Make();
        var user = await SeedUserAsync(db, "tuyere-cms@forge.local");

        db.SystemApiKeys.Add(new SystemApiKey
        {
            Name = "Tuyere", KeyHash = "h", KeyPrefix = "fsk_abc12345",
            UserId = user.Id, IsActive = true,
        });
        await db.SaveChangesAsync();

        var rows = await registry.ListAsync(CancellationToken.None);

        var row = rows.Single(r => r.Kind == IntegrationKind.SystemApiKey);
        row.OwnerEmail.Should().Be("tuyere-cms@forge.local");
        row.ManageRoute.Should().Be("/admin/system-api-keys");
    }

    [Fact]
    public async Task ListAsync_QuickBooksToken_AppearsWhenSettingPresent()
    {
        var (registry, _, settings) = Make();
        var connectedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var refreshedAt = DateTimeOffset.UtcNow.AddHours(-2);
        settings.Setup(s => s.FindByKeyAsync("qb_oauth_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting
            {
                Key = "qb_oauth_token",
                Value = "(encrypted blob)",
                CreatedAt = connectedAt,
                UpdatedAt = refreshedAt,
            });

        var rows = await registry.ListAsync(CancellationToken.None);

        var row = rows.SingleOrDefault(r => r.Kind == IntegrationKind.QuickBooksOAuth);
        row.Should().NotBeNull();
        row!.Name.Should().Be("QuickBooks Online");
        row.Status.Should().Be("Connected");
        row.ManageRoute.Should().Be("/admin/integrations");
        row.CreatedAt.Should().Be(connectedAt,
            "SystemSetting now extends BaseAuditableEntity; CreatedAt is the original connect-time");
        row.LastUsedAt.Should().Be(refreshedAt,
            "UpdatedAt on the token row is the most recent refresh / reconnect");
    }

    [Fact]
    public async Task ListAsync_NoQuickBooksToken_DoesNotEmitRow()
    {
        var (registry, _, _) = Make();
        var rows = await registry.ListAsync(CancellationToken.None);
        rows.Should().NotContain(r => r.Kind == IntegrationKind.QuickBooksOAuth);
    }

    [Fact]
    public async Task ListAsync_AllKindsTogether_AppearAndSortByMostRecentlyTouched()
    {
        var (registry, db, settings) = Make();
        var user = await SeedUserAsync(db);

        // Clearly-newest LastUsedAt — should sort to the top regardless of
        // which order CreatedAt timestamps land in across the SaveChanges call.
        var clearlyLatest = DateTimeOffset.UtcNow.AddYears(1);
        db.SystemApiKeys.Add(new SystemApiKey
        {
            Name = "recent", KeyHash = "h", KeyPrefix = "fsk_1",
            UserId = user.Id, IsActive = true, LastUsedAt = clearlyLatest,
        });
        // Old, never-used — should sort to the bottom.
        db.BiApiKeys.Add(new BiApiKey
        {
            Name = "stale", KeyHash = "h", KeyPrefix = "qbe_2",
            IsActive = true,
        });
        db.EdiTradingPartners.Add(new EdiTradingPartner
        {
            Name = "Acme EDI",
            QualifierId = "ZZ", QualifierValue = "ACME",
            IsActive = true,
        });
        settings.Setup(s => s.FindByKeyAsync("qb_oauth_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "qb_oauth_token", Value = "x" });
        await db.SaveChangesAsync();

        var rows = await registry.ListAsync(CancellationToken.None);

        rows.Select(r => r.Kind).Should().Contain(new[]
        {
            IntegrationKind.SystemApiKey,
            IntegrationKind.BiApiKey,
            IntegrationKind.EdiTradingPartner,
            IntegrationKind.QuickBooksOAuth,
        });
        rows.First().Name.Should().Be("recent",
            "the most-recently-touched row sorts first regardless of kind");
    }
}
