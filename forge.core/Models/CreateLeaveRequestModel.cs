namespace Forge.Core.Models;

public record CreateLeaveRequestModel(
    int PolicyId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Hours,
    string? Reason);
