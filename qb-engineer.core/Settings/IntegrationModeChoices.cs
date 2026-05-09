namespace QBEngineer.Core.Settings;

/// <summary>
/// Phase 1m — shared enum-choices for the per-integration mode toggle
/// (Mock / Real / Disabled). Replaces the global <c>MockIntegrations</c>
/// boolean flag with a per-integration selector so a single install can
/// run real QuickBooks against mock SMTP, etc.
/// </summary>
public static class IntegrationModeChoices
{
    public const string Mock = "Mock";
    public const string Real = "Real";
    public const string Disabled = "Disabled";

    public static IReadOnlyList<EnumChoice> All { get; } =
    [
        new(Mock, "Mock — return canned data, useful for dev / demo"),
        new(Real, "Real — use actual provider credentials below"),
        new(Disabled, "Disabled — feature is off; controllers return 409"),
    ];
}
