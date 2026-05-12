namespace Forge.Core.Models;

public record ShiftAssignmentResponseModel(
    int Id,
    int UserId,
    string UserName,
    int ShiftId,
    string ShiftName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? ShiftDifferentialRate,
    string? Notes);
