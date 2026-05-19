using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Default implementation of <see cref="IExternalIdentityResolver"/>. Routes
/// each provider to its underlying token source:
///
///   - <b>QuickBooks</b> → delegates to <see cref="IQuickBooksTokenService"/>
///     (existing service that already handles refresh / revoke / encryption
///     against the <c>qb_oauth_token</c> system_settings row).
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
    IQuickBooksTokenService quickBooksTokenService,
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

            // Drive / Calendar / Gmail-OAuth / Xero / FreshBooks / Sage /
            // Zoho / NetSuite all still flow through their existing
            // provider-specific token plumbing today. They'll move under
            // this resolver as part of the Phase 2 follow-ups (OAuth-init
            // MediatR command + per-provider token stores).
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

    private ResolvedExternalIdentity? LogUnsupportedProvider(string provider)
    {
        logger.LogDebug(
            "[EXTERNAL-IDENTITY] No resolver path wired for provider '{Provider}' yet — " +
            "callers should keep using the provider-specific token plumbing for now",
            provider);
        return null;
    }
}
