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
        new(OperationTrackingKey, Group, "Operation tracking (per-step status and timers)", SettingDataType.Boolean,
            Description: "When on, each job shows its routing operations on the desktop job card, phone and "
                + "terminal: operators start and stop a timer per operation (several can run at once), record "
                + "quantities done and scrap, and mark operations complete or skipped. Board cards show "
                + "operation progress and estimated time left. Off = job-level timers only.",
            DefaultValue: "true",
            SortOrder: 100),
    ];
}
