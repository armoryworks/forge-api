namespace Forge.Core.Models;

public record CreateClockEventRequestModel(
    string EventTypeCode,
    string? Reason,
    string? ScanMethod,
    string? Source);
