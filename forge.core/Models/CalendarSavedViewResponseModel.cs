namespace Forge.Core.Models;

/// <summary>
/// compliance-calendar A-3: a saved overlay-layer selection (personal or role-default).
/// </summary>
public record CalendarSavedViewResponseModel(
    int Id,
    string Name,
    int? OwnerUserId,
    string? RoleKey,
    string Scope,
    int[] SelectedSuperGroupIds,
    int[] SelectedEventTypeIds,
    bool IsDefault);
