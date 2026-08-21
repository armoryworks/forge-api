namespace Forge.Core.Models;

/// <summary>
/// One selectable value for an <see cref="IntegrationSettingField"/> whose
/// <c>InputType == "enum"</c>. Decoupled from the server-side
/// <c>EnumChoice</c> in <c>Forge.Core.Settings</c> so the API contract stays
/// stable across internal refactors of the descriptor model.
/// </summary>
public record IntegrationSettingChoice(string Value, string Label);

public record IntegrationSettingField(
    string Key,
    string Label,
    string Value,
    bool IsSensitive,
    bool IsRequired,
    string InputType = "text",
    /// <summary>
    /// Non-null only when <see cref="InputType"/> is <c>"enum"</c>. The
    /// admin UI renders a select dropdown over these choices. Order is
    /// significant — preserved from the descriptor.
    /// </summary>
    IReadOnlyList<IntegrationSettingChoice>? Choices = null,
    /// <summary>
    /// Optional human-readable hint shown beneath the field in the admin
    /// dialog. Sourced from <c>SettingDescriptor.Description</c>.
    /// </summary>
    string? Description = null
);

public record IntegrationStatusModel(
    string Provider,
    string Name,
    string Description,
    string Icon,
    bool IsConfigured,
    List<IntegrationSettingField> Fields,
    string Category = "service",
    List<string>? SandboxSteps = null,
    string? SandboxUrl = null,
    string? LogoUrl = null,
    /// <summary>Capability whose being ON makes this integration needed. Null =
    /// infrastructure integration with no gating capability.</summary>
    string? CapabilityCode = null,
    /// <summary>Whether that gating capability is currently enabled (true when
    /// there is no gating capability).</summary>
    bool CapabilityEnabled = true,
    /// <summary>Readiness verdict: NotNeeded / Configured / Mock / Gap / Optional.
    /// "Gap" is the actionable state — capability on, unconfigured, in production.</summary>
    string Readiness = "Mock"
);

public record IntegrationSettingsResult(
    bool ShowSandboxGuides,
    List<IntegrationStatusModel> Integrations,
    /// <summary>True when running a production posture (production env, not globally
    /// forced to mock) — the UI surfaces gaps as actionable rather than informational.</summary>
    bool ProductionPosture = false,
    /// <summary>True when MockIntegrations=true while ASPNETCORE_ENVIRONMENT=Production —
    /// a misconfiguration the admin panel should warn about.</summary>
    bool MockIntegrationsInProduction = false,
    /// <summary>Count of integrations in the "Gap" state — drives the admin banner.</summary>
    int GapCount = 0
);

public record UpdateIntegrationSettingsRequestModel(
    Dictionary<string, string> Settings
);

public record TestIntegrationResultModel(
    bool Success,
    string Message
);
