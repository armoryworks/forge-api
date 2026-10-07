namespace Forge.Core.Models;

/// <summary>
/// Everything the first-run module picker renders: the modules themselves and the
/// one-click bundles that pre-select a common combination of them.
/// </summary>
public record SetupModulesResponseModel(
    IReadOnlyList<SetupModuleResponseModel> Modules,
    IReadOnlyList<SetupModuleBundleResponseModel> Bundles);
