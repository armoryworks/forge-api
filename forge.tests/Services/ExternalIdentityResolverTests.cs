using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// Coverage for the foundational external-identity routing — Phase 2a.
/// Verifies the QuickBooks delegation, policy semantics for an install-
/// only provider, and the unsupported-provider null-return contract.
/// </summary>
public class ExternalIdentityResolverTests
{
    private readonly Mock<IQuickBooksTokenService> _qb = new();
    private readonly Mock<ICloudStorageTokenManager> _cloudTokens = new();

    private ExternalIdentityResolver MakeResolver(Forge.Data.Context.AppDbContext db) =>
        new(db, _qb.Object, _cloudTokens.Object, NullLogger<ExternalIdentityResolver>.Instance);

    /// <summary>Overload for the prior-arity tests that don't touch the DB.</summary>
    private ExternalIdentityResolver MakeResolver() => MakeResolver(TestDbContextFactory.Create());

    [Fact]
    public async Task ResolveAsync_QuickBooks_ReturnsTokenWithRealmId_OnConnectedInstall()
    {
        _qb.Setup(s => s.GetValidAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("qb-access-token");
        _qb.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuickBooksTokenData(
                AccessToken: "qb-access-token",
                RefreshToken: "qb-refresh",
                RealmId: "1234567890",
                AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(45),
                RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(80)));

        var result = await MakeResolver()
            .ResolveAsync("quickbooks", userId: null, TokenResolutionPolicy.InstallOnly);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("qb-access-token");
        result.Provider.Should().Be("quickbooks");
        result.ScopeUsed.Should().Be(TokenScope.Install,
            "QuickBooks only supports install-wide tokens — one company per Forge install");
        result.RealmOrTenantId.Should().Be("1234567890",
            "RealmId surfaces so QB-scoped REST callers don't re-fetch it");
        result.UserId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_QuickBooks_ReturnsNull_WhenNotConnected()
    {
        _qb.Setup(s => s.GetValidAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await MakeResolver()
            .ResolveAsync("quickbooks", userId: null);

        result.Should().BeNull(
            "no token = no resolved identity; callers handle null by prompting the user to connect");
    }

    [Fact]
    public async Task ResolveAsync_QuickBooks_WithRequireUserPolicy_ReturnsNull()
    {
        // QB is install-only. RequireUser policy is incoherent for it —
        // resolver returns null rather than silently flipping to install.
        _qb.Setup(s => s.GetValidAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("would-have-returned-this");

        var result = await MakeResolver()
            .ResolveAsync("quickbooks", userId: 42, TokenResolutionPolicy.RequireUser);

        result.Should().BeNull(
            "RequireUser on an install-only provider must NOT fall back to install — that would " +
            "violate the caller's audit-trail contract");
        _qb.Verify(s => s.GetValidAccessTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "resolver should short-circuit before hitting the QB service when policy is incoherent");
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedProvider_ReturnsNull()
    {
        // Drive / Calendar / Gmail-OAuth / Xero etc. are not yet routed
        // through this resolver. Returning null keeps callers safe — they
        // continue using the provider-specific token plumbing.
        var result = await MakeResolver()
            .ResolveAsync("gdrive", userId: 42);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_EmptyProvider_Throws()
    {
        var act = () => MakeResolver().ResolveAsync("", userId: null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Drive (gdrive) ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_Drive_PerUser_ReturnsUserScopedToken()
    {
        using var db = TestDbContextFactory.Create();
        var provider = new CloudStorageProvider
        {
            ProviderCode = "gdrive",
            Mode = CloudStorageProviderMode.PerUser,
            IsActive = true,
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();

        var link = new UserCloudStorageLink
        {
            UserId = 42,
            ProviderId = provider.Id,
            OAuthTokenEncrypted = "ENC", RefreshTokenEncrypted = "ENC",
        };
        db.Set<UserCloudStorageLink>().Add(link);
        await db.SaveChangesAsync();

        _cloudTokens.Setup(t => t.GetValidAccessTokenAsync(
                It.Is<UserCloudStorageLink>(l => l.UserId == 42),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-user-access-token");

        var result = await MakeResolver(db)
            .ResolveAsync("gdrive", userId: 42, TokenResolutionPolicy.PreferUser);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("drive-user-access-token");
        result.ScopeUsed.Should().Be(TokenScope.User);
        result.UserId.Should().Be(42);
    }

    [Fact]
    public async Task ResolveAsync_Drive_PreferUser_FallsBackToInstall_WhenNoUserLink()
    {
        using var db = TestDbContextFactory.Create();
        var provider = new CloudStorageProvider
        {
            ProviderCode = "gdrive",
            Mode = CloudStorageProviderMode.ServiceAccount,
            IsActive = true,
            OAuthTokenEncrypted = "ENC", RefreshTokenEncrypted = "ENC",
            RootFolderId = "shared-drive-root-xyz",
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();

        _cloudTokens.Setup(t => t.GetValidAccessTokenAsync(
                It.Is<CloudStorageProvider>(p => p.ProviderCode == "gdrive"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-install-access-token");

        var result = await MakeResolver(db)
            .ResolveAsync("gdrive", userId: 99, TokenResolutionPolicy.PreferUser);

        result.Should().NotBeNull();
        result!.ScopeUsed.Should().Be(TokenScope.Install,
            "no user link → PreferUser falls back to install scope");
        result.AccessToken.Should().Be("drive-install-access-token");
        result.RealmOrTenantId.Should().Be("shared-drive-root-xyz",
            "the install-scope Drive carries the Shared Drive root folder id as 'realm or tenant'");
    }

    [Fact]
    public async Task ResolveAsync_Drive_RequireUser_DoesNotFallBackToInstall()
    {
        using var db = TestDbContextFactory.Create();
        var provider = new CloudStorageProvider
        {
            ProviderCode = "gdrive",
            Mode = CloudStorageProviderMode.ServiceAccount,
            IsActive = true,
            OAuthTokenEncrypted = "ENC", RefreshTokenEncrypted = "ENC",
        };
        db.CloudStorageProviders.Add(provider);
        await db.SaveChangesAsync();

        // RequireUser + no link → null, even though install scope is available.
        var result = await MakeResolver(db)
            .ResolveAsync("gdrive", userId: 99, TokenResolutionPolicy.RequireUser);

        result.Should().BeNull(
            "RequireUser must refuse to fall back to install scope — that would violate " +
            "the caller's attribution contract (e.g. sending email from the wrong identity)");
    }

    [Fact]
    public async Task ResolveAsync_Drive_PerUserMode_RequireInstall_ReturnsNull()
    {
        // A Drive provider configured in PerUser mode never populates the
        // install-scope OAuth columns. RequireInstall against this shape
        // returns null rather than trying to use empty tokens.
        using var db = TestDbContextFactory.Create();
        db.CloudStorageProviders.Add(new CloudStorageProvider
        {
            ProviderCode = "gdrive",
            Mode = CloudStorageProviderMode.PerUser,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var result = await MakeResolver(db)
            .ResolveAsync("gdrive", userId: null, TokenResolutionPolicy.RequireInstall);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_Drive_NoActiveProvider_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        // No provider row at all — admin hasn't configured Drive yet.
        var result = await MakeResolver(db).ResolveAsync("gdrive", userId: 1);
        result.Should().BeNull();
    }
}
