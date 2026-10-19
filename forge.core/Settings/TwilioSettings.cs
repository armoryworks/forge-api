namespace Forge.Core.Settings;

public static class TwilioSettings
{
    public const string Group = "Voice — Twilio";

    public const string KeyMode = "twilio.mode";
    public const string KeyAccountSid = "twilio.account-sid";
    public const string KeyAuthToken = "twilio.auth-token";
    public const string KeyRequireSignature = "twilio.require-signature";
    public const string KeyWebhookUrl = "twilio.webhook-url";

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
            Description: "When ON, webhooks are rejected with 401 unless an Auth Token is set and X-Twilio-Signature verifies. With no Auth Token, unsigned webhooks are only accepted in Mock mode with this OFF.",
            DataType: SettingDataType.Boolean,
            DefaultValue: "false",
            SortOrder: 20),

        new(
            Key: KeyWebhookUrl,
            Group: Group,
            DisplayName: "Webhook URL",
            Description: "The exact Voice Status Callback URL set in Twilio, e.g. https://erp.example.com/api/v1/communications/webhook/twilio. Signatures are checked against it. Set it when Forge sits behind a proxy or tunnel; blank rebuilds the URL from the request.",
            DataType: SettingDataType.String,
            SortOrder: 21),
    ];
}
