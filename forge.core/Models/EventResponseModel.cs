namespace Forge.Core.Models;

public record EventResponseModel(
    int Id,
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Location,
    string EventType,
    bool IsRequired,
    bool IsCancelled,
    int CreatedByUserId,
    string CreatedByName,
    List<EventAttendeeResponseModel> Attendees,
    DateTimeOffset CreatedAt);

public record EventAttendeeResponseModel(
    int Id,
    int UserId,
    string UserName,
    string Status,
    DateTimeOffset? RespondedAt);
