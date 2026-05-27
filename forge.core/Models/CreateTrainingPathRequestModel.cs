namespace Forge.Core.Models;

/// <summary>F-14-BE-01: payload for creating a training path (admin). ModuleIds set the
/// path's modules in order; null/empty creates an empty path.</summary>
public record CreateTrainingPathRequestModel(
    string Title,
    string Slug,
    string? Description,
    string? Icon,
    bool IsAutoAssigned,
    bool IsActive,
    int SortOrder,
    string? AllowedRoles,
    List<int>? ModuleIds);
