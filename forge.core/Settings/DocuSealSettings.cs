namespace Forge.Core.Settings;

public static class DocuSealSettings
{
    public const string Group = "E-Signature — DocuSeal";

    public const string KeyMode = "docuseal.mode";
    public const string KeyApiUrl = "docuseal.api-url";
    public const string KeyPublicBaseUrl = "docuseal.public-base-url";
    public const string KeyApiKey = "docuseal.api-key";
    public const string KeyWebhookSecret = "docuseal.webhook-secret";
    public const string KeyTimeoutSeconds = "docuseal.timeout-seconds";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Choices: IntegrationModeChoices.All, SortOrder: 0),
        new(KeyApiUrl, Group, "API URL", SettingDataType.Url,
            DefaultValue: "http://forge-signing:3000",
            Description: "DocuSeal instance base URL — server-to-server. Docker-cluster default.",
            SortOrder: 10),
        new(KeyPublicBaseUrl, Group, "Public Base URL", SettingDataType.String,
            Description: "Browser-reachable URL when behind a reverse proxy. Used to rewrite embed iframe URLs. Leave blank if DocuSeal is directly accessible on API URL.",
            SortOrder: 11),
        new(KeyApiKey, Group, "API Key", SettingDataType.Secret, IsSecret: true, SortOrder: 12),
        new(KeyWebhookSecret, Group, "Webhook Secret", SettingDataType.Secret, IsSecret: true,
            Description: "Optional — signs DocuSeal callbacks so the system can verify authenticity.",
            SortOrder: 13),
        new(KeyTimeoutSeconds, Group, "Timeout (seconds)", SettingDataType.Integer,
            DefaultValue: "30",
            SortOrder: 14),
    ];
}
