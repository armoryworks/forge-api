using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Default implementation of <see cref="IExternalIdentityStore"/>. The
/// write-side companion to <see cref="ExternalIdentityResolver"/>.
///
/// Provider routing matches the resolver: QuickBooks writes go through
/// <see cref="IQuickBooksTokenService"/>'s existing persistence path;
/// other providers will be added as their OAuth flows are migrated under
/// the unified initiate/complete-command machinery.
///
/// Save semantics:
///   - Install scope → one row per provider; subsequent saves overwrite.
///   - User scope → one row per (user, provider); subsequent saves
///     overwrite for the same user. Disconnect removes the row entirely.
/// </summary>
public class ExternalIdentityStore(
    IQuickBooksTokenService quickBooksTokenService,
    ILogger<ExternalIdentityStore> logger) : IExternalIdentityStore
{
    public async Task SaveInstallTokenAsync(
        string provider, ExternalIdentityToken token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));

        switch (provider.ToLowerInvariant())
        {
            case "quickbooks":
                await quickBooksTokenService.SaveTokenAsync(
                    new QuickBooksTokenData(
                        AccessToken: token.AccessToken,
                        RefreshToken: token.RefreshToken
                            ?? throw new InvalidOperationException(
                                "QuickBooks refresh token is required."),
                        RealmId: token.RealmOrTenantId
                            ?? throw new InvalidOperationException(
                                "QuickBooks RealmId is required (returned by the OAuth callback)."),
                        AccessTokenExpiresAt: token.AccessTokenExpiresAt
                            ?? DateTimeOffset.UtcNow.AddMinutes(60),
                        RefreshTokenExpiresAt: token.RefreshTokenExpiresAt
                            ?? DateTimeOffset.UtcNow.AddDays(100)),
                    ct);
                return;

            default:
                throw new NotSupportedException(
                    $"Install-token save not yet routed for provider '{provider}'. " +
                    "Add a provider-specific token service + dispatch case here.");
        }
    }

    public Task SaveUserTokenAsync(
        string provider, int userId, ExternalIdentityToken token, CancellationToken ct)
    {
        // Per-user token persistence will land alongside the per-user
        // Drive / Calendar / Gmail-OAuth flows. For now no provider
        // routes through the user scope.
        throw new NotSupportedException(
            $"User-token save not yet routed for provider '{provider}'. " +
            "Per-user token storage is a Phase 2 follow-up.");
    }

    public async Task DeleteInstallTokenAsync(string provider, CancellationToken ct)
    {
        switch (provider.ToLowerInvariant())
        {
            case "quickbooks":
                await quickBooksTokenService.ClearTokenAsync(ct);
                return;

            default:
                logger.LogWarning(
                    "[EXTERNAL-IDENTITY] Disconnect requested for provider '{Provider}' " +
                    "but no install-token path is routed yet — no-op",
                    provider);
                return;
        }
    }

    public Task DeleteUserTokenAsync(string provider, int userId, CancellationToken ct)
    {
        throw new NotSupportedException(
            $"User-token delete not yet routed for provider '{provider}'.");
    }
}
