namespace QBEngineer.Core.Settings;

/// <summary>
/// Phase 1m — shipping carrier credentials. UPS / FedEx / DHL / Stamps.com
/// each have their own auth shape but share the Mode toggle pattern.
/// </summary>
public static class ShippingSettings
{
    private const string Group = "Shipping Carriers";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        // UPS — OAuth 2.0 client_credentials (since Jan 2024, deprecated old XML API)
        new("ups.mode", Group, "UPS Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: 100),
        new("ups.client-id", Group, "UPS Client ID", SettingDataType.String, SortOrder: 110),
        new("ups.client-secret", Group, "UPS Client Secret", SettingDataType.Secret, IsSecret: true, SortOrder: 111),
        new("ups.account-number", Group, "UPS Account Number", SettingDataType.String, SortOrder: 112),

        // FedEx — OAuth 2.0
        new("fedex.mode", Group, "FedEx Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: 200),
        new("fedex.client-id", Group, "FedEx Client ID (API Key)", SettingDataType.String, SortOrder: 210),
        new("fedex.client-secret", Group, "FedEx Client Secret", SettingDataType.Secret, IsSecret: true, SortOrder: 211),
        new("fedex.account-number", Group, "FedEx Account Number", SettingDataType.String, SortOrder: 212),

        // DHL — API key
        new("dhl.mode", Group, "DHL Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: 300),
        new("dhl.api-key", Group, "DHL API Key", SettingDataType.Secret, IsSecret: true, SortOrder: 310),
        new("dhl.account-number", Group, "DHL Account Number", SettingDataType.String, SortOrder: 311),

        // Stamps.com — username + password
        new("stamps.mode", Group, "Stamps.com Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: 400),
        new("stamps.username", Group, "Stamps.com Username", SettingDataType.String, SortOrder: 410),
        new("stamps.password", Group, "Stamps.com Password", SettingDataType.Secret, IsSecret: true, SortOrder: 411),
        new("stamps.integration-id", Group, "Stamps.com Integration ID", SettingDataType.String, SortOrder: 412),
    ];
}
