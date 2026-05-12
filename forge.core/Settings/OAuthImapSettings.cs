namespace Forge.Core.Settings;

/// <summary>
/// Phase 1m — admin-managed settings for OAuth-IMAP (Gmail + Microsoft 365).
/// Pairs with <see cref="Models.Communications.OAuthImapOptions"/>; the
/// settings service hydrates that record from these keys.
/// </summary>
public static class OAuthImapSettings
{
    public const string Group = "Email — OAuth";

    public const string KeyRedirectUri = "oauth-imap.redirect-uri";
    public const string KeyGoogleClientId = "oauth-imap.google.client-id";
    public const string KeyGoogleClientSecret = "oauth-imap.google.client-secret";
    public const string KeyMicrosoftClientId = "oauth-imap.microsoft.client-id";
    public const string KeyMicrosoftClientSecret = "oauth-imap.microsoft.client-secret";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(
            Key: KeyRedirectUri,
            Group: Group,
            DisplayName: "Redirect URI",
            Description: "Public-facing OAuth callback URL. Must be registered with both Google and Microsoft developer consoles. Format: https://your-domain/account/communications/oauth-callback",
            DataType: SettingDataType.Url,
            SortOrder: 0),

        new(
            Key: KeyGoogleClientId,
            Group: Group,
            DisplayName: "Google Client ID",
            Description: "Google Cloud OAuth 2.0 client ID — register at console.cloud.google.com with Gmail API enabled and the https://mail.google.com/ scope authorized.",
            DataType: SettingDataType.String,
            SortOrder: 10),

        new(
            Key: KeyGoogleClientSecret,
            Group: Group,
            DisplayName: "Google Client Secret",
            Description: "Paired with Google Client ID. Stored encrypted server-side.",
            DataType: SettingDataType.Secret,
            IsSecret: true,
            SortOrder: 11),

        new(
            Key: KeyMicrosoftClientId,
            Group: Group,
            DisplayName: "Microsoft Client ID",
            Description: "Azure App Registration client ID — register at portal.azure.com with IMAP.AccessAsUser.All + offline_access scopes.",
            DataType: SettingDataType.String,
            SortOrder: 20),

        new(
            Key: KeyMicrosoftClientSecret,
            Group: Group,
            DisplayName: "Microsoft Client Secret",
            Description: "Paired with Microsoft Client ID. Stored encrypted server-side.",
            DataType: SettingDataType.Secret,
            IsSecret: true,
            SortOrder: 21),
    ];
}
