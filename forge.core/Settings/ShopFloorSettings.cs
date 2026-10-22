using Forge.Core.Enums;

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
    public const string BackflushDefaultKey = "shop-floor.backflush-default";
    public const string LaborSplitKey = "shop-floor.labor-split";
    public const string BreaksPauseLaborKey = "shop-floor.breaks-pause-labor";
    public const string RecentJobDaysKey = "shop-floor.recent-job-days";
    public const string StepGatingKey = "shop-floor.step-gating";
    public const string StaleTimerHoursKey = "shop-floor.stale-timer-hours";
    public const string LaborSplitWeighted = "Weighted";
    public const string LaborSplitNone = "None";
    public const int DefaultStaleTimerHours = 14;

    public static IReadOnlyList<SettingDescriptor> Descriptors =>
    [
        new(OperationTrackingKey, Group, "Operation tracking (per-step status and timers)", SettingDataType.Boolean,
            Description: "When on, each job shows its routing operations on the desktop job card, phone and "
                + "terminal: operators start and stop a timer per operation (several can run at once), record "
                + "quantities done and scrap, and mark operations complete or skipped. Board cards show "
                + "operation progress and estimated time left. Off = job-level timers only.",
            DefaultValue: "true",
            SortOrder: 100),
        new(BackflushDefaultKey, Group, "Backflush default for BOM components", SettingDataType.Enum,
            Description: "Applies to parts whose backflush setting is Default, while Backflush completion is on. "
                + "Auto takes the components out of stock when a production run is received or the job's last "
                + "operation is completed; Manual and None leave them for the operator to issue.",
            DefaultValue: "Manual",
            SortOrder: 110,
            Choices: [new("Auto", "Auto"), new("Manual", "Manual"), new("None", "None")]),
        new(LaborSplitKey, Group, "Labor split for overlapping timers", SettingDataType.Enum,
            Description: "How a person's clocked time is costed when several of their timers run at once. Weighted "
                + "shares it across the open timers by each step's operator attendance % (an even split when every "
                + "step is fully attended), so a job is never charged more labor than the person was paid for. None "
                + "charges every timer its full elapsed time. Applies to timers stopped after the change; use "
                + "Recompute labor to re-price an open period.",
            DefaultValue: LaborSplitWeighted,
            SortOrder: 120,
            Choices: [new(LaborSplitWeighted, "Weighted"), new(LaborSplitNone, "None")]),
        new(BreaksPauseLaborKey, Group, "Unpaid breaks remove labor from jobs", SettingDataType.Boolean,
            Description: "When on, time spent on a break or lunch is not charged to the jobs whose timers kept "
                + "running; machine burden still accrues. Turn off if your shop pays for breaks.",
            DefaultValue: "true",
            SortOrder: 130),
        new(RecentJobDaysKey, Group, "Recent jobs on the kiosk (days)", SettingDataType.Integer,
            Description: "The kiosk's My jobs list includes jobs the person logged time on within this many days, "
                + "next to the jobs assigned to them and the jobs they have running.",
            DefaultValue: "3",
            SortOrder: 140,
            ValidationPattern: "^([0-9]|[1-2][0-9]|30)$"),
        new(StepGatingKey, Group, "Step gating", SettingDataType.Enum,
            Description: "Whether a routing step can report more pieces than the steps it must follow have completed. "
                + "Off never checks. Warn asks the operator to confirm and logs the reason. Block refuses unless a "
                + "Manager or Production Manager overrides it with a reason. A Complete link also keeps a step from "
                + "being marked Done before that predecessor is done. Starting a timer is never gated. Each part "
                + "can override this on its routing.",
            DefaultValue: nameof(StepGatingMode.Off),
            SortOrder: 150,
            Choices:
            [
                new(nameof(StepGatingMode.Off), "Off"),
                new(nameof(StepGatingMode.Warn), "Warn"),
                new(nameof(StepGatingMode.Block), "Block"),
            ]),
        new(StaleTimerHoursKey, Group, "Stale timer alert (hours)", SettingDataType.Integer,
            Description: "Managers and production managers get a notification when a timer has been running this "
                + "many hours, and the labor reconciliation report counts it. Timers are never stopped automatically.",
            DefaultValue: "14",
            SortOrder: 160,
            ValidationPattern: "^([1-9]|[1-6][0-9]|7[0-2])$"),
    ];
}
