namespace Forge.Core.Models;

public record ClockEventResponseModel(
    int Id,
    int UserId,
    string UserName,
    string EventTypeCode,
    string? Reason,
    string? ScanMethod,
    DateTimeOffset Timestamp,
    string? Source);
