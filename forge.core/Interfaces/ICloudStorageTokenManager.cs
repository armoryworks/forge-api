using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

/// <summary>
/// Pro Services rollout — resolves a usable (decrypted, non-expired)
/// OAuth access token for a cloud-storage provider. Handles the encrypt/
/// decrypt boundary and the refresh-token rotation that real providers
/// (Google Drive / OneDrive / Dropbox) require.
///
/// <para>Both lifecycle modes are supported:</para>
/// <list type="bullet">
///   <item><b>Service-account</b> — token lives on <see cref="CloudStorageProvider"/>
///         columns; one shared token across all install operations.</item>
///   <item><b>Per-user</b> — token lives on <see cref="UserCloudStorageLink"/>;
///         each user has their own.</item>
/// </list>
///
/// <para>Each call returns a token that's valid for at least the next
/// <c>5 minutes</c> (refresh-buffer). If the access token is expired or
/// near-expiry, the implementation refreshes (via the provider's
/// <c>RefreshTokenAsync</c>), re-encrypts the new tokens, and persists.
/// Returns null when:
/// <list type="bullet">
///   <item>The provider row has no usable refresh token (admin must reconnect)</item>
///   <item>The refresh call itself fails (logged)</item>
///   <item>The provider implementation isn't registered (resolver miss)</item>
/// </list></para>
/// </summary>
public interface ICloudStorageTokenManager
{
    /// <summary>
    /// Service-account mode — returns a valid access token from
    /// <see cref="CloudStorageProvider.OAuthTokenEncrypted"/>, refreshing
    /// if needed. Persists rotation back to the provider row.
    /// </summary>
    Task<string?> GetValidAccessTokenAsync(CloudStorageProvider provider, CancellationToken ct);

    /// <summary>
    /// Per-user mode — returns a valid access token from
    /// <see cref="UserCloudStorageLink.OAuthTokenEncrypted"/>, refreshing
    /// if needed. Persists rotation back to the link row.
    /// </summary>
    Task<string?> GetValidAccessTokenAsync(UserCloudStorageLink link, CancellationToken ct);
}
