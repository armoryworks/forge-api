namespace Forge.Core.Models;

public record CreateShiftAssignmentRequestModel(
    int UserId,
    int ShiftId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? ShiftDifferentialRate,
    string? Notes);
