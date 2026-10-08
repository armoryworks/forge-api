namespace Forge.Core.Settings;

/// <summary>
/// Shop-floor behaviour switches. Operation tracking adds per-operation timers, progress counts and
/// the board card's progress line; while it is off the phone and terminal behave exactly as before
/// and the operation write endpoints refuse.
/// </summary>
public static class ShopFloorSettings
{
    private static readonly string Group = "Shop Floor";

    public const string OperationTrackingKey = "shop-floor.operation-tracking";

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(OperationTrackingKey, Group, "Operation tracking on phone and terminal", SettingDataType.Boolean,
            Description: "When on, scanning a job offers its routing operations: operators start and stop a "
                + "timer per operation (several can run at once), record quantities done and scrap, and mark "
                + "operations complete. Board cards show operation progress and estimated time left. Off "
                + "(default) = job-level timers only, as before.",
            DefaultValue: "false",
            SortOrder: 100),
    ];
}
