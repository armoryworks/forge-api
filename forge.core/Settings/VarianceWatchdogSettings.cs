namespace Forge.Core.Settings;

/// <summary>
/// §10.6 — thresholds for the variance watchdog (the daily job that nudges the Controller role when a
/// standard-cost variance account drifts). An account triggers when its period net exceeds
/// max(absolute floor, percent × period COGS); when COGS is zero only the floor applies. The watchdog's
/// dedupe state lives in a raw <c>system_settings</c> row (not registered here — it is job state, not
/// an admin-tunable setting).
/// </summary>
public static class VarianceWatchdogSettings
{
    private static readonly string Group = "Accounting";

    public const string PercentOfCogsKey = "accounting.variance-watchdog.percent-of-cogs";
    public const string AbsoluteFloorKey = "accounting.variance-watchdog.absolute-floor";

    public const decimal DefaultPercentOfCogs = 5m;
    public const decimal DefaultAbsoluteFloor = 500m;

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(PercentOfCogsKey, Group, "Variance Watchdog — % of COGS", SettingDataType.String,
            Description: "A variance account triggers a Controller notification when its period net exceeds "
                + "this percentage of the period's COGS (subject to the absolute floor).",
            DefaultValue: "5",
            ValidationPattern: @"^\d+(\.\d+)?$",
            SortOrder: 900),
        new(AbsoluteFloorKey, Group, "Variance Watchdog — Absolute Floor", SettingDataType.String,
            Description: "Minimum functional-currency amount a variance account must reach before the "
                + "watchdog notifies, regardless of COGS. The only threshold applied when period COGS is zero.",
            DefaultValue: "500",
            ValidationPattern: @"^\d+(\.\d+)?$",
            SortOrder: 901),
    ];
}
