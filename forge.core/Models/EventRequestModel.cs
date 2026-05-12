namespace Forge.Core.Models;

public record EventRequestModel(
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Location,
    string EventType,
    bool IsRequired,
    List<int> AttendeeUserIds);
