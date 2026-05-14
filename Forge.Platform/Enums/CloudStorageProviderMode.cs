namespace Forge.Platform.Enums;

/// <summary>
/// Pro Services rollout (D3) — how a cloud storage provider authenticates.
/// Per-user mode (default for most installs) has each user OAuth-link
/// their own account; folder operations run as that user. Service-account
/// mode has the install configure a single account whose credentials are
/// used for all operations (useful for headless / kiosk / shared-folder
/// scenarios).
/// </summary>
public enum CloudStorageProviderMode
{
    /// <summary>Per-user OAuth links via UserCloudStorageLink rows.</summary>
    PerUser = 1,
    /// <summary>Single install-level service account stored on the provider row.</summary>
    ServiceAccount = 2,
}
