namespace Forge.Api.Capabilities;

/// <summary>
/// A one-click preset for the first-run module picker: a named set of modules
/// that commonly go together (e.g. a job shop). Choosing a bundle only fills in
/// the picker's checkboxes; the shop can still adjust them before finishing.
/// </summary>
public record ModuleBundle(
    string Id,
    string Name,
    IReadOnlyList<string> ModuleIds);
