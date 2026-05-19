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
    string? LogoUrl = null
);

public record IntegrationSettingsResult(
    bool ShowSandboxGuides,
    List<IntegrationStatusModel> Integrations
);

public record UpdateIntegrationSettingsRequestModel(
    Dictionary<string, string> Settings
);

public record TestIntegrationResultModel(
    bool Success,
    string Message
);
