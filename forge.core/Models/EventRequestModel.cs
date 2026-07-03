namespace Forge.Core.Models;

public record EventRequestModel(
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Location,
    string EventType,
    bool IsRequired,
    List<int> AttendeeUserIds,
    // compliance-calendar A-1: configurable taxonomy. Optional during expand; the legacy
    // EventType enum is still sent/stored until the Stage-7 contract.
    int? EventTypeId = null);
