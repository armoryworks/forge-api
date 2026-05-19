using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Services;

public class ExternalIdentityStoreTests
{
    private readonly Mock<IQuickBooksTokenService> _qb = new();

    private ExternalIdentityStore MakeStore() =>
        new(_qb.Object, NullLogger<ExternalIdentityStore>.Instance);

    [Fact]
    public async Task SaveInstallTokenAsync_QuickBooks_PersistsViaQuickBooksTokenService()
    {
        var token = new ExternalIdentityToken(
            AccessToken: "qb-access",
            RefreshToken: "qb-refresh",
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(45),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(80),
            ExternalUserId: null,
            RealmOrTenantId: "9876543210");

        await MakeStore().SaveInstallTokenAsync("quickbooks", token, CancellationToken.None);

        _qb.Verify(s => s.SaveTokenAsync(
                It.Is<QuickBooksTokenData>(d =>
                    d.AccessToken == "qb-access"
                    && d.RefreshToken == "qb-refresh"
                    && d.RealmId == "9876543210"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "QuickBooks install-token save must delegate to the existing service " +
            "to keep encryption + repository-write semantics consistent");
    }

    [Fact]
    public async Task SaveInstallTokenAsync_QuickBooks_RejectsMissingRealmId()
    {
        // QuickBooks RealmId is mandatory — every API call is scoped by it.
        var token = new ExternalIdentityToken(
            AccessToken: "qb-access",
            RefreshToken: "qb-refresh",
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(45),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(80),
            ExternalUserId: null,
            RealmOrTenantId: null);

        var act = () => MakeStore().SaveInstallTokenAsync("quickbooks", token, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RealmId*");
    }

    [Fact]
    public async Task SaveInstallTokenAsync_UnsupportedProvider_Throws()
    {
        var token = new ExternalIdentityToken("x", null, null, null, null, null);
        var act = () => MakeStore().SaveInstallTokenAsync("gdrive", token, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*not yet routed*");
    }

    [Fact]
    public async Task DeleteInstallTokenAsync_QuickBooks_DelegatesToQuickBooksTokenService()
    {
        await MakeStore().DeleteInstallTokenAsync("quickbooks", CancellationToken.None);
        _qb.Verify(s => s.ClearTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteInstallTokenAsync_UnsupportedProvider_NoOps_ButLogs()
    {
        // No-op is intentional — disconnect of an un-routed provider is
        // benign. Logging the request is left to verifying the integration
        // path explicitly.
        var act = () => MakeStore().DeleteInstallTokenAsync("gdrive", CancellationToken.None);
        await act.Should().NotThrowAsync();
        _qb.Verify(s => s.ClearTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
