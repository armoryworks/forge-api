namespace Forge.Core.Settings;

/// <summary>
/// Pro Services rollout — Google Drive cloud-storage integration. All
/// admin-managed via the integrations dialog; no <c>appsettings.json</c>
/// edit required for normal setup.
///
/// Provider key in <see cref="IntegrationDescriptorCatalog"/> /
/// <see cref="IntegrationModeBootstrap"/>: <c>"gdrive"</c>.
/// </summary>
public static class GoogleDriveSettings
{
    public const string Group = "Cloud Storage — Google Drive";

    public const string KeyMode = "gdrive.mode";
    public const string KeyClientId = "gdrive.client-id";
    public const string KeyClientSecret = "gdrive.client-secret";
    public const string KeyScopes = "gdrive.scopes";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Description: "Mock returns canned folder responses (in-memory). Real calls Google Drive API v3 with per-user OAuth tokens.",
            Choices: IntegrationModeChoices.All, SortOrder: 0),
        new(KeyClientId, Group, "OAuth Client ID", SettingDataType.String,
            Description: "OAuth 2.0 Client ID from Google Cloud Console (apps.googleusercontent.com).",
            SortOrder: 10),
        new(KeyClientSecret, Group, "OAuth Client Secret", SettingDataType.Secret,
            IsSecret: true,
            Description: "Paired with the Client ID. Stored encrypted server-side.",
            SortOrder: 11),
        new(KeyScopes, Group, "OAuth Scopes", SettingDataType.String,
            DefaultValue: "https://www.googleapis.com/auth/drive.file",
            Description: "Space-separated OAuth scope(s). drive.file = least-privilege (only files this app creates); drive = full Drive access.",
            SortOrder: 12),
    ];
}
