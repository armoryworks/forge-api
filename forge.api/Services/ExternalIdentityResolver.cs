using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default implementation of <see cref="IExternalIdentityResolver"/>. Routes
/// each provider to its underlying token source:
///
///   - <b>QuickBooks</b> → delegates to <see cref="IQuickBooksTokenService"/>
///     (existing service that already handles refresh / revoke / encryption
///     against the <c>qb_oauth_token</c> system_settings row).
///   - <b>Google Drive</b> → routes via <see cref="ICloudStorageTokenManager"/>
///     using either the install-scoped <see cref="CloudStorageProvider"/>
///     row (ServiceAccount mode) or a per-user <see cref="UserCloudStorageLink"/>
///     row, depending on the requested <see cref="TokenResolutionPolicy"/>.
///   - <b>Other providers</b> → not yet routed. Adding a provider requires
///     wiring it into the <see cref="ResolveAsync"/> dispatch + (typically)
///     adding a parallel token service. The contract is intentionally narrow
///     so consumers can adopt incrementally.
///
/// The resolver is the consumer-facing read API. Writes happen via
/// <see cref="IExternalIdentityStore"/>, which is the write counterpart
/// invoked by OAuth-completion handlers when a new token arrives.
/// </summary>
public class ExternalIdentityResolver(
    AppDbContext db,
    IQuickBooksTokenService quickBooksTokenService,
    ICloudStorageTokenManager cloudStorageTokenManager,
    ILogger<ExternalIdentityResolver> logger) : IExternalIdentityResolver
{
    public async Task<ResolvedExternalIdentity?> ResolveAsync(
        string provider,
        int? userId,
        TokenResolutionPolicy policy = TokenResolutionPolicy.PreferUser,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));

        return provider.ToLowerInvariant() switch
        {
            "quickbooks" => await ResolveQuickBooksAsync(policy, ct),
            "gdrive" => await ResolveCloudStorageAsync("gdrive", userId, policy, ct),

            // Calendar / Gmail-OAuth / Xero / FreshBooks / Sage / Zoho /
            // NetSuite all still flow through their existing provider-specific
            // token plumbing today. They'll move under this resolver as
            // Phase 2 follow-ups land.
            _ => LogUnsupportedProvider(provider),
        };
    }

    private async Task<ResolvedExternalIdentity?> ResolveQuickBooksAsync(
        TokenResolutionPolicy policy,
        CancellationToken ct)
    {
        // QuickBooks is install-only — one company per Forge install. The
        // RequireUser policy is incoherent for this provider; reject it
        // rather than silently falling back to install (which would be a
        // surprising override of the caller's contract).
        if (policy == TokenResolutionPolicy.RequireUser)
        {
            logger.LogWarning(
                "[EXTERNAL-IDENTITY] QuickBooks does not support per-user tokens; " +
                "RequireUser policy is incoherent for this provider");
            return null;
        }

        var accessToken = await quickBooksTokenService.GetValidAccessTokenAsync(ct);
        if (accessToken is null) return null;

        // QuickBooksTokenService.GetValidAccessTokenAsync rotates the token
        // internally if it was near expiry, so the returned string is fresh.
        // Surface the RealmId so the caller (e.g. a service hitting QB's
        // company-scoped REST endpoints) doesn't have to re-fetch it.
        var tokenData = await quickBooksTokenService.GetTokenAsync(ct);
        return new ResolvedExternalIdentity(
            AccessToken: accessToken,
            Provider: "quickbooks",
            ScopeUsed: TokenScope.Install,
            UserId: null,
            RealmOrTenantId: tokenData?.RealmId);
    }

    /// <summary>
    /// Cloud-storage provider resolution. Supports BOTH scopes:
    ///   - Install (Forge-as-author): provider row with Mode=ServiceAccount,
    ///     tokens stored on CloudStorageProvider directly.
    ///   - User (user-as-author): per-user UserCloudStorageLink keyed on
    ///     (userId, providerId).
    ///
    /// Policy resolution:
    ///   - PreferUser + userId set → try user, fall back to install
    ///   - RequireUser → user only; null if not connected
    ///   - RequireInstall → install only
    ///   - InstallOnly → install only (synonym for RequireInstall on
    ///     dual-scope providers)
    /// </summary>
    private async Task<ResolvedExternalIdentity?> ResolveCloudStorageAsync(
        string providerCode,
        int? userId,
        TokenResolutionPolicy policy,
        CancellationToken ct)
    {
        var provider = await db.CloudStorageProviders
            .FirstOrDefaultAsync(p => p.ProviderCode == providerCode && p.IsActive, ct);

        if (provider is null)
        {
            logger.LogDebug(
                "[EXTERNAL-IDENTITY] No active CloudStorageProvider row for '{Code}' — " +
                "admin needs to configure the install-level provider first",
                providerCode);
            return null;
        }

        // Try per-user first if policy permits + userId supplied.
        if (userId.HasValue
            && (policy == TokenResolutionPolicy.PreferUser
                || policy == TokenResolutionPolicy.RequireUser))
        {
            var link = await db.Set<UserCloudStorageLink>()
                .Include(l => l.Provider)
                .FirstOrDefaultAsync(l =>
                    l.UserId == userId.Value
                    && l.ProviderId == provider.Id, ct);

            if (link is not null)
            {
                var userToken = await cloudStorageTokenManager.GetValidAccessTokenAsync(link, ct);
                if (userToken is not null)
                {
                    return new ResolvedExternalIdentity(
                        AccessToken: userToken,
                        Provider: providerCode,
                        ScopeUsed: TokenScope.User,
                        UserId: userId,
                        RealmOrTenantId: null);
                }
            }

            if (policy == TokenResolutionPolicy.RequireUser)
            {
                // Caller demanded user scope; no fallback to install.
                logger.LogDebug(
                    "[EXTERNAL-IDENTITY] No user OAuth grant for user {UserId} on provider '{Code}' — RequireUser policy fails",
                    userId, providerCode);
                return null;
            }
        }

        // Install scope. Only valid when the provider was configured in
        // ServiceAccount mode (PerUser mode never populates the install-
        // level OAuth columns).
        if (policy == TokenResolutionPolicy.RequireUser)
        {
            // Already handled above; defensive.
            return null;
        }

        if (provider.Mode != CloudStorageProviderMode.ServiceAccount)
        {
            // Per-user-only configuration; no install token available.
            return null;
        }

        var installToken = await cloudStorageTokenManager.GetValidAccessTokenAsync(provider, ct);
        if (installToken is null) return null;

        return new ResolvedExternalIdentity(
            AccessToken: installToken,
            Provider: providerCode,
            ScopeUsed: TokenScope.Install,
            UserId: null,
            RealmOrTenantId: provider.RootFolderId);
    }

    private ResolvedExternalIdentity? LogUnsupportedProvider(string provider)
    {
        logger.LogDebug(
            "[EXTERNAL-IDENTITY] No resolver path wired for provider '{Provider}' yet — " +
            "callers should keep using the provider-specific token plumbing for now",
            provider);
        return null;
    }
}
