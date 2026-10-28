namespace Forge.Core.Settings;

/// <summary>
/// Quote settings. <c>quotes.default_valid_days</c> is how long a configured (CPQ) quote stays valid.
/// </summary>
public static class QuoteSettings
{
    public const string DefaultValidDaysKey = "quotes.default_valid_days";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } =
    [
        new(DefaultValidDaysKey, "Sales", "Quote validity (days)", SettingDataType.Integer,
            Description: "Days a configured (CPQ) quote stays valid from the day it is made.",
            DefaultValue: "30",
            ValidationPattern: @"^[1-9]\d{0,2}$",
            SortOrder: 10),
    ];
}
