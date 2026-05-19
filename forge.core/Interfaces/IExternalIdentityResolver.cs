using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Single point of "give me a fresh access token for {provider} to act on
/// behalf of {caller}". Picks between install-wide and per-user token
/// storage based on the supplied <see cref="TokenResolutionPolicy"/>,
/// proactively refreshes if the cached token is near expiry, and returns
/// a <see cref="ResolvedExternalIdentity"/> that the caller passes
/// straight to the provider's service.
///
/// Replaces the prior pattern of every service injecting
/// <see cref="Microsoft.Extensions.Options.IOptions{T}"/> with the raw
/// token blob — that pattern couldn't distinguish per-user from install
/// and had no refresh awareness.
/// </summary>
public interface IExternalIdentityResolver
{
    /// <summary>
    /// Resolve a usable access token for the given provider, optionally
    /// scoped to a specific user. Returns null when no matching token
    /// exists — caller decides whether that's an error.
    /// </summary>
    /// <param name="provider">
    /// Provider key — matches the <c>IntegrationDescriptorCatalog</c>
    /// Provider field. E.g. <c>"quickbooks"</c>, <c>"gdrive"</c>,
    /// <c>"gcal"</c>, <c>"gmail-oauth"</c>.
    /// </param>
    /// <param name="userId">
    /// User on whose behalf the operation is happening. Null for
    /// unattended/system operations (background jobs, webhook handlers,
    /// system reflexes). The resolver uses this together with the
    /// <paramref name="policy"/> to pick between User and Install scopes.
    /// </param>
    /// <param name="policy">
    /// How the resolver should reconcile <paramref name="userId"/>
    /// against available token storage. See
    /// <see cref="TokenResolutionPolicy"/> for the policy semantics.
    /// </param>
    /// <returns>
    /// The resolved token + scope information, or null when no token is
    /// available for the requested policy. The resolver does NOT throw
    /// for "not connected" — callers handle that by checking for null
    /// (e.g. show a "Connect to {provider}" prompt) or by promoting
    /// null to an exception per their UX requirement.
    /// </returns>
    Task<ResolvedExternalIdentity?> ResolveAsync(
        string provider,
        int? userId,
        TokenResolutionPolicy policy = TokenResolutionPolicy.PreferUser,
        CancellationToken ct = default);
}

/// <summary>
/// Write-side counterpart to <see cref="IExternalIdentityResolver"/> —
/// persists tokens captured by OAuth-completion handlers into the right
/// storage layer (system_settings for install scope; UserIntegration /
/// UserCloudStorageLink for user scope). One implementation routes by
/// provider + scope.
/// </summary>
public interface IExternalIdentityStore
{
    /// <summary>Persist an install-wide token (encrypted) for the given provider.</summary>
    Task SaveInstallTokenAsync(string provider, ExternalIdentityToken token, CancellationToken ct);

    /// <summary>Persist a per-user token (encrypted) for the given provider + user.</summary>
    Task SaveUserTokenAsync(string provider, int userId, ExternalIdentityToken token, CancellationToken ct);

    /// <summary>Remove an install-wide token (disconnect).</summary>
    Task DeleteInstallTokenAsync(string provider, CancellationToken ct);

    /// <summary>Remove a per-user token (user disconnects their account).</summary>
    Task DeleteUserTokenAsync(string provider, int userId, CancellationToken ct);
}
