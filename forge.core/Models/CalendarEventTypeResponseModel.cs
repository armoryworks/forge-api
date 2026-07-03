namespace Forge.Core.Models;

/// <summary>compliance-calendar A-1: an Event-Type within a Super-Group layer.</summary>
public record CalendarEventTypeResponseModel(
    int Id,
    int SuperGroupId,
    string Key,
    string Name,
    string? Color,
    bool RequiresTracking,
    int SortOrder);
