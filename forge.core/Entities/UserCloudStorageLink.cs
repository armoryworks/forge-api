namespace Forge.Core.Entities;

/// <summary>
/// Pro Services rollout (Artifact 4 §3.3 / D3) — per-user OAuth link to a
/// cloud-storage provider running in per-user mode. One row per
/// (user, provider) pair.
///
/// <para>Tokens encrypted at rest via <c>ITokenEncryptionService</c>.</para>
/// </summary>
public class UserCloudStorageLink : BaseAuditableEntity
{
    public Guid UserId { get; set; }

    public int ProviderId { get; set; }

    /// <summary>Provider-side user identifier (Google account email, Microsoft Graph user id, Dropbox account id).</summary>
    public string? ExternalUserId { get; set; }

    /// <summary>OAuth access token (encrypted).</summary>
    public string OAuthTokenEncrypted { get; set; } = string.Empty;

    /// <summary>OAuth refresh token (encrypted).</summary>
    public string RefreshTokenEncrypted { get; set; } = string.Empty;

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public CloudStorageProvider Provider { get; set; } = null!;
}
