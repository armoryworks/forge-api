namespace Forge.Core.Models;

/// <summary>
/// The module -> capability mapping behind the picker, used to bake the stubbed
/// demo's capability descriptor so its cordoning matches a real install. Each
/// module's list is its fully-resolved enabled set (Foundations ∪ module ∪
/// dependency closure); the demo unions the active modules' lists.
/// </summary>
public record SetupModuleCapabilitiesResponseModel(
    IReadOnlyList<SetupModuleCapabilityResponseModel> Capabilities,
    IReadOnlyList<string> Foundations,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Modules);

public record SetupModuleCapabilityResponseModel(string Code, string Area, string Name, bool IsDefaultOn);
