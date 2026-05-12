using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Pro Services rollout (Artifact 4 / D9) — provider-agnostic seam for
/// cloud-storage folder operations. Implementations: Google Drive,
/// OneDrive (Microsoft Graph), Dropbox, plus an in-memory mock used in
/// tests / dev mode.
///
/// <para>The interface stays thin: folder CRUD, OAuth token refresh,
/// health check. Higher-order concerns — per-entity link routing, dual-
/// path auto-create (per D2), hybrid storage selection — live in the
/// application layer (a MultiCloudStorageService aggregator and the
/// entity_cloud_links table).</para>
///
/// <para>Each provider is resolved by its <see cref="ProviderCode"/>
/// (<c>"gdrive"</c> / <c>"onedrive"</c> / <c>"dropbox"</c>). DI registers
/// all implementations; the resolver picks the right one per
/// <c>CloudStorageProvider</c> row.</para>
///
/// <para>Token lifecycle: callers pass an already-decrypted access token
/// (the service does NOT touch ITokenEncryptionService directly). For
/// 401 responses, callers invoke <see cref="RefreshTokenAsync"/>, re-
/// encrypt, persist, and retry. Keeping crypto out of the provider seam
/// makes mocking + per-call retries easier.</para>
/// </summary>
public interface ICloudStorageIntegrationService
{
    /// <summary>Provider code this implementation handles, e.g. <c>"gdrive"</c>.</summary>
    string ProviderCode { get; }

    /// <summary>Create a folder on the provider. Honors <see cref="CreateFolderRequest.EnsureExists"/>.</summary>
    Task<CloudFolder> CreateFolderAsync(
        string accessToken,
        CreateFolderRequest request,
        CancellationToken ct);

    /// <summary>Look up a folder by its provider-side ID.</summary>
    Task<CloudFolder?> GetFolderAsync(
        string accessToken,
        string folderExternalId,
        CancellationToken ct);

    /// <summary>List the immediate child folders (folders only — not files) of a folder.</summary>
    Task<IReadOnlyList<CloudFolder>> ListChildFoldersAsync(
        string accessToken,
        string parentExternalId,
        CancellationToken ct);

    /// <summary>
    /// Resolve a folder by path, walking the tree from root. Returns null
    /// when any segment is missing. Used by the auto-create flow to detect
    /// existing folders before creating new ones.
    /// </summary>
    Task<CloudFolder?> FindFolderByPathAsync(
        string accessToken,
        string path,
        CancellationToken ct);

    /// <summary>Refresh an expired OAuth access token using its refresh token.</summary>
    Task<CloudStorageTokenRefreshResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct);

    /// <summary>Health check — pings the provider with the given access token to verify connectivity.</summary>
    Task<CloudStorageHealthResult> HealthCheckAsync(
        string accessToken,
        CancellationToken ct);
}
