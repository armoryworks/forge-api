namespace Forge.Core.Settings;

public static class UspsSettings
{
    public const string Group = "Address Validation — USPS";

    public const string KeyMode = "usps.mode";
    public const string KeyConsumerKey = "usps.consumer-key";
    public const string KeyConsumerSecret = "usps.consumer-secret";

    /// <summary>
    /// Legacy alias retained because the IntegrationDescriptorCatalog +
    /// IOptions-shim already wired this name. Points at ConsumerKey
    /// — the real OAuth credential the USPS Addresses API v3 expects.
    /// </summary>
    public const string KeyUserId = KeyConsumerKey;

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(
            Key: KeyMode,
            Group: Group,
            DisplayName: "Mode",
            Description: "Mock validates format only (state codes, ZIP regex). Real calls USPS Addresses API v3 over OAuth client-credentials.",
            DataType: SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Choices: IntegrationModeChoices.All,
            SortOrder: 0),

        new(
            Key: KeyConsumerKey,
            Group: Group,
            DisplayName: "Consumer Key",
            Description: "USPS Customer Onboarding Portal app credential. Register at cop.usps.com.",
            DataType: SettingDataType.String,
            SortOrder: 10),

        new(
            Key: KeyConsumerSecret,
            Group: Group,
            DisplayName: "Consumer Secret",
            Description: "Paired with Consumer Key. Stored encrypted server-side.",
            DataType: SettingDataType.Secret,
            IsSecret: true,
            SortOrder: 11),
    ];
}
