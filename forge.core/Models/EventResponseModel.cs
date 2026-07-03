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
    DateTimeOffset CreatedAt,
    // compliance-calendar A-1/A-3: configurable taxonomy for overlay-layer filtering.
    int? EventTypeId = null,
    int? SuperGroupId = null,
    // compliance-calendar A-4: tiered workflow surface (null/false for reminder-tier).
    string? Status = null,
    int? OwnerUserId = null,
    bool IsBlocking = false,
    DateTimeOffset? AcknowledgedAt = null);

public record EventAttendeeResponseModel(
    int Id,
    int UserId,
    string UserName,
    string Status,
    DateTimeOffset? RespondedAt);
