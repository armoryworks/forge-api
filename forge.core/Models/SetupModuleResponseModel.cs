namespace Forge.Core.Models;

/// <summary>
/// A module offered by the first-run module picker. Plain-language, capability
/// codes intentionally omitted — the picker shows the choice, the server resolves
/// it to capabilities at apply time.
/// </summary>
public record SetupModuleResponseModel(
    string Id,
    string Name,
    string Summary,
    string PrerequisiteNote,
    bool DefaultSelected);
