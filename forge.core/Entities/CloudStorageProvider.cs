
namespace Forge.Core.Entities;

/// <summary>
/// Pro Services rollout (Artifact 4 §3.2 / D9) — install-level configured
/// cloud-storage provider. One row per provider per install (multiple
/// rows on hybrid-storage installs; per-entity routing via
/// <see cref="EntityCloudLink"/>).
///
/// <para>Per-user mode keeps <see cref="OAuthTokenEncrypted"/> /
/// <see cref="RefreshTokenEncrypted"/> null — each user's tokens live
/// on <see cref="UserCloudStorageLink"/>. Service-account mode populates
/// these fields with the install-level credentials.</para>
///
/// <para>Tokens are encrypted at rest via <c>ITokenEncryptionService</c>
/// (ASP.NET Data Protection — keys live in Postgres).</para>
/// </summary>
public class CloudStorageProvider : BaseAuditableEntity
{
    /// <summary>One of: "gdrive" | "onedrive" | "dropbox".</summary>
    public string ProviderCode { get; set; } = string.Empty;

    public CloudStorageProviderMode Mode { get; set; } = CloudStorageProviderMode.PerUser;

    public bool IsActive { get; set; } = true;

    /// <summary>Provider-side root folder id (where forge folders are anchored).</summary>
    public string? RootFolderId { get; set; }

    /// <summary>Provider-side service-account identifier (only when <see cref="Mode"/> = ServiceAccount).</summary>
    public string? ServiceAccountId { get; set; }

    /// <summary>OAuth access token (encrypted). Populated only when <see cref="Mode"/> = ServiceAccount.</summary>
    public string? OAuthTokenEncrypted { get; set; }

    /// <summary>OAuth refresh token (encrypted). Populated only when <see cref="Mode"/> = ServiceAccount.</summary>
    public string? RefreshTokenEncrypted { get; set; }

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public DateTimeOffset? LastConnectedAt { get; set; }

    /// <summary>Last error encountered during a folder operation; cleared on next successful op.</summary>
    public string? LastError { get; set; }

    /// <summary>Provider-specific settings (JSONB): folder ID format hints, scope grants, region prefs, etc.</summary>
    public string? Settings { get; set; }

    public ICollection<UserCloudStorageLink> UserLinks { get; set; } = [];
    public ICollection<EntityCloudLink> EntityLinks { get; set; } = [];
}
