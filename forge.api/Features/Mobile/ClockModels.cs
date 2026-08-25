namespace Forge.Api.Features.Mobile;

/// <summary>State is out | in | break; the phone renders it in huge type.</summary>
public record ClockStateResponseModel(
    string State,
    string? LastEventType,
    DateTimeOffset? LastEventAt,
    int? LastEventId);

public record ClockPunchResponseModel(int EventId, ClockStateResponseModel State);
