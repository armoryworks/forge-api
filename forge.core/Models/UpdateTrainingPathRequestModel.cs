namespace Forge.Core.Models;

/// <summary>F-14-BE-01: payload for editing a training path (admin). When ModuleIds is
/// non-null it replaces the path's module set (in order); null leaves modules unchanged.</summary>
public record UpdateTrainingPathRequestModel(
    string Title,
    string Slug,
    string? Description,
    string? Icon,
    bool IsAutoAssigned,
    bool IsActive,
    int SortOrder,
    string? AllowedRoles,
    List<int>? ModuleIds);
