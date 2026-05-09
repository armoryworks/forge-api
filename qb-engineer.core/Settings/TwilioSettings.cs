namespace QBEngineer.Core.Settings;

public static class TwilioSettings
{
    public const string Group = "Voice — Twilio";

    public const string KeyMode = "twilio.mode";
    public const string KeyAccountSid = "twilio.account-sid";
    public const string KeyAuthToken = "twilio.auth-token";
    public const string KeyRequireSignature = "twilio.require-signature";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(
            Key: KeyMode,
            Group: Group,
            DisplayName: "Mode",
            Description: "Mock returns canned webhook payloads; Real validates X-Twilio-Signature on inbound webhooks.",
            DataType: SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All,
            SortOrder: 0),

        new(
            Key: KeyAccountSid,
            Group: Group,
            DisplayName: "Account SID",
            Description: "Informational; Twilio Account SID is not used for inbound webhook validation.",
            DataType: SettingDataType.String,
            SortOrder: 10),

        new(
            Key: KeyAuthToken,
            Group: Group,
            DisplayName: "Auth Token",
            Description: "Twilio auth token. Required for X-Twilio-Signature verification on inbound webhooks.",
            DataType: SettingDataType.Secret,
            IsSecret: true,
            SortOrder: 11),

        new(
            Key: KeyRequireSignature,
            Group: Group,
            DisplayName: "Require Signature",
            Description: "When ON, webhooks with missing/invalid X-Twilio-Signature are rejected with 401. Recommended for production.",
            DataType: SettingDataType.Boolean,
            DefaultValue: "false",
            SortOrder: 20),
    ];
}
