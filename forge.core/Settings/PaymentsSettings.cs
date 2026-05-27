namespace Forge.Core.Settings;

/// <summary>
/// P06-5 — admin-selectable policy controlling whether a recorded payment may be
/// amended or voided after creation. Stored at <c>payments.modification-policy</c>
/// in <c>system_settings</c>; tighten it to prevent abuse of post-recording edits.
/// </summary>
public static class PaymentsSettings
{
    public const string ModificationPolicyKey = "payments.modification-policy";

    public const string PolicyLocked = "locked";
    public const string PolicyAmendOnly = "amend_only";
    public const string PolicyFull = "full";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } = new[]
    {
        new SettingDescriptor(
            Key: ModificationPolicyKey,
            Group: "Payments",
            DisplayName: "Payment modification policy",
            DataType: SettingDataType.Enum,
            Description: "Controls whether a recorded payment can be amended or voided after creation. " +
                         "Tighten this to prevent abuse of post-recording edits.",
            DefaultValue: PolicyFull,
            Choices: new[]
            {
                new EnumChoice(PolicyLocked, "Locked — no changes after recording"),
                new EnumChoice(PolicyAmendOnly, "Amend only — edit, but no void"),
                new EnumChoice(PolicyFull, "Amend & void"),
            }),
    };
}
