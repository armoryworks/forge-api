namespace Forge.Core.Models;

/// <summary>
/// A one-click module bundle offered above the first-run module picker. Selecting
/// it checks exactly the listed modules.
/// </summary>
public record SetupModuleBundleResponseModel(
    string Id,
    string Name,
    IReadOnlyList<string> ModuleIds);
