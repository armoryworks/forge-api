namespace Forge.Core.Models;

/// <summary>
/// compliance-calendar A-3: a Super-Group layer (with its Event-Types) for the overlay
/// calendar's layer list. Only groups the current user may see are returned.
/// </summary>
public record CalendarSuperGroupResponseModel(
    int Id,
    string Key,
    string Name,
    string? Color,
    string? IconName,
    bool DefaultVisible,
    bool RequiresTracking,
    int SortOrder,
    IReadOnlyList<CalendarEventTypeResponseModel> EventTypes);
