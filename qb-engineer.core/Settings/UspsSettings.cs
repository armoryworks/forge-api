namespace QBEngineer.Core.Settings;

public static class UspsSettings
{
    public const string Group = "Address Validation — USPS";

    public const string KeyMode = "usps.mode";
    public const string KeyUserId = "usps.user-id";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(
            Key: KeyMode,
            Group: Group,
            DisplayName: "Mode",
            Description: "Mock validates format only (state codes, ZIP regex). Real calls USPS Web Tools Address Information API v3.",
            DataType: SettingDataType.Enum,
            DefaultValue: IntegrationModeChoices.Mock,
            Choices: IntegrationModeChoices.All,
            SortOrder: 0),

        new(
            Key: KeyUserId,
            Group: Group,
            DisplayName: "USPS User ID",
            Description: "USPS Web Tools User ID. Free registration at https://www.usps.com/business/web-tools-apis/.",
            DataType: SettingDataType.String,
            SortOrder: 10),
    ];
}
