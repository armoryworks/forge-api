using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Services;

/// <summary>
/// Coverage for the foundational external-identity routing — Phase 2a.
/// Verifies the QuickBooks delegation, policy semantics for an install-
/// only provider, and the unsupported-provider null-return contract.
/// </summary>
public class ExternalIdentityResolverTests
{
    private readonly Mock<IQuickBooksTokenService> _qb = new();

    private ExternalIdentityResolver MakeResolver() =>
        new(_qb.Object, NullLogger<ExternalIdentityResolver>.Instance);

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
}
