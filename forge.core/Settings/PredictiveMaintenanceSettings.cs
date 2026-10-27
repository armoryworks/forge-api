namespace Forge.Core.Settings;

/// <summary>
/// Thresholds for the built-in rule-based predictive maintenance. <c>maintenance.predictive.asset-class-overrides</c>
/// sets any of them for one asset class, as JSON keyed by asset type then threshold, e.g.
/// <c>{"Tooling": {"usage-threshold-percent": 85}}</c>; a class or threshold it leaves out uses the install value.
/// </summary>
public static class PredictiveMaintenanceSettings
{
    private static readonly string Group = "Maintenance";

    public const string Prefix = "maintenance.predictive.";
    public const string UsageThresholdPercentKey = "usage-threshold-percent";
    public const string DowntimeTrendFactorKey = "downtime-trend-factor";
    public const string CycleDriftRatioKey = "cycle-drift-ratio";
    public const string CycleDriftRunsKey = "cycle-drift-runs";
    public const string ScrapTrendFactorKey = "scrap-trend-factor";
    public const string SuppressionDaysKey = "suppression-days";
    public const string AssetClassOverridesKey = Prefix + "asset-class-overrides";

    public const decimal DefaultUsageThresholdPercent = 90m;
    public const decimal DefaultDowntimeTrendFactor = 2m;
    public const decimal DefaultCycleDriftRatio = 1.15m;
    public const int DefaultCycleDriftRuns = 5;
    public const decimal DefaultScrapTrendFactor = 2m;
    public const int DefaultSuppressionDays = 30;

    private const string DecimalPattern = @"^\d{1,4}(\.\d{1,4})?$";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(Prefix + UsageThresholdPercentKey, Group, "Predict service due at % of the PM interval", SettingDataType.String,
            Description: "A prediction is raised when the shots or hours since the last service reach this percent of a "
                + "preventive maintenance interval. The due date is projected from the last 30 days of use.",
            DefaultValue: "90", ValidationPattern: DecimalPattern, SortOrder: 100),
        new(Prefix + DowntimeTrendFactorKey, Group, "Rising breakdown factor", SettingDataType.String,
            Description: "A prediction is raised when unplanned downtime in the last 30 days (count or minutes) is at "
                + "least this many times the 30-day average of the 90 days before. Needs two or more stops.",
            DefaultValue: "2", ValidationPattern: DecimalPattern, SortOrder: 110),
        new(Prefix + CycleDriftRatioKey, Group, "Cycle-time drift ratio", SettingDataType.String,
            Description: "A prediction is raised when the actual cycle time over the last production runs is more "
                + "than this multiple of the ideal cycle time.",
            DefaultValue: "1.15", ValidationPattern: DecimalPattern, SortOrder: 120),
        new(Prefix + CycleDriftRunsKey, Group, "Runs checked for cycle-time drift", SettingDataType.Integer,
            Description: "How many of the most recent timed production runs the cycle-time drift rule averages.",
            DefaultValue: "5", ValidationPattern: @"^\d{1,3}$", SortOrder: 130),
        new(Prefix + ScrapTrendFactorKey, Group, "Rising scrap factor", SettingDataType.String,
            Description: "A prediction is raised when the scrap rate on a tool's runs in the last 30 days is at least "
                + "this many times its rate over the 90 days before (or 5% when it had no scrap before).",
            DefaultValue: "2", ValidationPattern: DecimalPattern, SortOrder: 140),
        new(Prefix + SuppressionDaysKey, Group, "Days a false positive silences a rule", SettingDataType.Integer,
            Description: "After a prediction is marked a false positive, the same rule raises nothing for that asset "
                + "for this many days.",
            DefaultValue: "30", ValidationPattern: @"^\d{1,3}$", SortOrder: 150),
        new(AssetClassOverridesKey, Group, "Per asset class thresholds", SettingDataType.Json,
            Description: "Optional JSON that sets the thresholds above for one asset class, keyed by asset type, e.g. "
                + "{\"Tooling\": {\"usage-threshold-percent\": 85, \"scrap-trend-factor\": 1.5}}.",
            DefaultValue: "{}", SortOrder: 160),
    ];
}
