namespace Forge.Core.Entities;

/// <summary>
/// Pro Services rollout (Artifact 4 §3.3 / D3) — per-user OAuth link to a
/// cloud-storage provider running in per-user mode. One row per
/// (user, provider) pair.
///
/// <para>Tokens encrypted at rest via <c>ITokenEncryptionService</c>.</para>
///
/// <para><b>UserId is <c>int</c></b> to match <c>ApplicationUser.Id</c>
/// (ASP.NET Identity's int-keyed primary key on the users table). The
/// pre-Phase-2c column was <c>Guid</c> — an Artifact 4 mistake that
/// never matched the actual user keyspace — but no code wrote rows
/// against it, so the refactor was a clean int-conversion. See migration
/// <c>UserCloudStorageLinkUserIdToInt</c>.</para>
/// </summary>
public class UserCloudStorageLink : BaseAuditableEntity
{
    public int UserId { get; set; }

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
