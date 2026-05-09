namespace QBEngineer.Core.Settings;

public static class DocuSealSettings
{
    public const string Group = "E-Signature — DocuSeal";

    public const string KeyMode = "docuseal.mode";
    public const string KeyApiUrl = "docuseal.api-url";
    public const string KeyApiKey = "docuseal.api-key";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(KeyMode, Group, "Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Choices: IntegrationModeChoices.All, SortOrder: 0),
        new(KeyApiUrl, Group, "API URL", SettingDataType.Url,
            Description: "DocuSeal instance base URL — typically https://docuseal.com or your self-hosted host.",
            SortOrder: 10),
        new(KeyApiKey, Group, "API Key", SettingDataType.Secret, IsSecret: true, SortOrder: 11),
    ];
}
