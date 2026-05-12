namespace Forge.Core.Models;

/// <summary>
/// Pro Services rollout (Artifact 4) — provider-agnostic cloud-storage
/// models. Each ICloudStorageIntegrationService implementation translates
/// between these types and its provider-specific API shape.
/// </summary>

/// <summary>One folder on a cloud-storage provider.</summary>
/// <param name="ExternalId">Provider-side folder ID (Google Drive fileId, Graph driveItemId, Dropbox path or id).</param>
/// <param name="Name">Display name (last segment of the path).</param>
/// <param name="Path">Full path from the root, e.g. <c>"/Customers/ACME/Project-42"</c>.</param>
/// <param name="WebUrl">URL the user can open in their browser.</param>
/// <param name="ParentExternalId">Parent folder's ExternalId, or null for root.</param>
public sealed record CloudFolder(
    string ExternalId,
    string Name,
    string Path,
    string WebUrl,
    string? ParentExternalId);

/// <summary>Request to create a folder on a provider.</summary>
/// <param name="Name">Folder name (single segment, no path separators).</param>
/// <param name="ParentExternalId">ID of the parent folder, or null to create at root.</param>
/// <param name="EnsureExists">If true, return the existing folder instead of erroring when a folder with this name already exists in the parent.</param>
public sealed record CreateFolderRequest(
    string Name,
    string? ParentExternalId,
    bool EnsureExists = true);

/// <summary>Result of an OAuth token refresh.</summary>
/// <param name="AccessToken">New access token (caller is responsible for encrypting before persistence).</param>
/// <param name="RefreshToken">New refresh token; some providers rotate, others return the same value.</param>
/// <param name="ExpiresAt">Absolute expiry time of the new access token.</param>
public sealed record CloudStorageTokenRefreshResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

/// <summary>Health-check result for a configured provider.</summary>
/// <param name="IsHealthy">True if the provider responded successfully.</param>
/// <param name="ProviderCode">Echoed provider code for logging clarity.</param>
/// <param name="Message">Human-readable status message; populated on failure.</param>
/// <param name="LatencyMs">Round-trip time of the health probe.</param>
public sealed record CloudStorageHealthResult(
    bool IsHealthy,
    string ProviderCode,
    string Message,
    long LatencyMs);
