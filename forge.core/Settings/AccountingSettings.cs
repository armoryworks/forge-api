namespace Forge.Core.Settings;

/// <summary>
/// Phase 1m — accounting providers (QuickBooks Online, Xero, FreshBooks,
/// Sage, NetSuite, Wave, Zoho). Each provider has the same shape:
///   - Mode (Mock / Real / Disabled)
///   - ClientId / ClientSecret (OAuth where applicable)
///   - Realm / Tenant / Account ID (provider-specific account scope)
///
/// Per the accounting boundary: at most one provider can be in
/// <c>Real</c> mode at a time (mutex enforced via the existing
/// CAP-ACCT-EXTERNAL ⊥ CAP-ACCT-BUILTIN capability pair). The admin UI
/// surfaces this — picking Real on one provider flips the others to
/// Disabled.
/// </summary>
public static class AccountingSettings
{
    private static readonly string Group = "Accounting";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        // QuickBooks Online (the existing primary)
        ..ProviderBlock("quickbooks", "QuickBooks Online", sortBase: 100),
        new("quickbooks.realm-id", Group, "QuickBooks Realm ID", SettingDataType.String,
            Description: "QBO Company / Realm ID issued at OAuth-connect time.",
            SortOrder: 105),

        // Xero
        ..ProviderBlock("xero", "Xero", sortBase: 200),
        new("xero.tenant-id", Group, "Xero Tenant ID", SettingDataType.String,
            Description: "Xero organisation tenant ID.",
            SortOrder: 205),

        // FreshBooks
        ..ProviderBlock("freshbooks", "FreshBooks", sortBase: 300),
        new("freshbooks.account-id", Group, "FreshBooks Account ID", SettingDataType.String,
            SortOrder: 305),

        // Sage
        ..ProviderBlock("sage", "Sage", sortBase: 400),
        new("sage.country-code", Group, "Sage Country Code", SettingDataType.String,
            DefaultValue: "US",
            Description: "Two-letter Sage region code (US / GB / DE / FR / ES …).",
            SortOrder: 405),

        // NetSuite — uses Token-Based Authentication, distinct shape (no OAuth)
        new("netsuite.mode", Group, "NetSuite Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: 500),
        new("netsuite.account-id", Group, "NetSuite Account ID", SettingDataType.String,
            Description: "NetSuite account ID, e.g. 1234567 or 1234567_SB1 for sandbox.",
            SortOrder: 510),
        new("netsuite.consumer-key", Group, "NetSuite Consumer Key", SettingDataType.String, SortOrder: 511),
        new("netsuite.consumer-secret", Group, "NetSuite Consumer Secret", SettingDataType.Secret, IsSecret: true, SortOrder: 512),
        new("netsuite.token-id", Group, "NetSuite Token ID", SettingDataType.String, SortOrder: 513),
        new("netsuite.token-secret", Group, "NetSuite Token Secret", SettingDataType.Secret, IsSecret: true, SortOrder: 514),

        // Wave — personal access token (not OAuth client/secret). Pre-fix
        // descriptor used the generic ProviderBlock which exposes ClientId
        // + ClientSecret fields that WaveOptions doesn't carry — admin
        // saves were persisted but the Wave service couldn't pick them
        // up because the property names don't exist.
        new("wave.mode", Group, "Wave Mode", SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: 600),
        new("wave.access-token", Group, "Wave Access Token", SettingDataType.Secret, IsSecret: true,
            Description: "Wave personal access token (or OAuth2 access token). Generated at developer.waveapps.com.",
            SortOrder: 610),
        new("wave.business-id", Group, "Wave Business ID", SettingDataType.String,
            Description: "Wave businessId — the GraphQL business node ID to scope queries to.",
            SortOrder: 611),

        // Zoho
        ..ProviderBlock("zoho", "Zoho Books", sortBase: 700),
        new("zoho.organization-id", Group, "Zoho Organization ID", SettingDataType.String, SortOrder: 705),
        new("zoho.data-center", Group, "Zoho Data Center", SettingDataType.String,
            DefaultValue: "com",
            Description: "Zoho region: com (US), eu, in, com.au, jp. Drives the OAuth + API base URLs.",
            SortOrder: 706),
    ];

    /// <summary>Common Mode + ClientId + ClientSecret triplet used by
    /// every OAuth-style accounting provider.</summary>
    private static IEnumerable<SettingDescriptor> ProviderBlock(
        string keyPrefix, string displayName, int sortBase) =>
    [
        new($"{keyPrefix}.mode", Group, $"{displayName} Mode",
            DataType: SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Disabled,
            Choices: IntegrationModeChoices.All, SortOrder: sortBase),
        new($"{keyPrefix}.client-id", Group, $"{displayName} Client ID",
            DataType: SettingDataType.String, SortOrder: sortBase + 1),
        new($"{keyPrefix}.client-secret", Group, $"{displayName} Client Secret",
            DataType: SettingDataType.Secret, IsSecret: true, SortOrder: sortBase + 2),
    ];
}
