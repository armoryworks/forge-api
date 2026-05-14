namespace Forge.Identity.Entities;

public class UserIntegration : BaseAuditableEntity
{
    public int UserId { get; set; }

    /// <summary>
    /// Integration category: "calendar", "messaging", "storage", "other"
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Provider identifier: "google_calendar", "slack", "dropbox", etc.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// User-provided label, e.g. "Work Calendar"
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// TokenEncryptionService-encrypted JSON containing OAuth tokens, API keys, etc.
    /// Never exposed in admin-facing queries.
    /// </summary>
    public string EncryptedCredentials { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// Provider-specific configuration (non-sensitive), e.g. sync folder path, calendar ID.
    /// </summary>
    public string? ConfigJson { get; set; }
}
