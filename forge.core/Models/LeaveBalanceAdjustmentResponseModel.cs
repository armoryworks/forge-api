using Forge.Core.Enums;

namespace Forge.Core.Models;

public record LeaveBalanceAdjustmentResponseModel(
    int Id,
    int UserId,
    int PolicyId,
    string PolicyName,
    LeaveAdjustmentType Type,
    decimal Hours,
    DateOnly EffectiveDate,
    string? Reason,
    decimal BalanceAfter,
    int? CreatedByUserId,
    string? CreatedByName,
    DateTimeOffset CreatedAt);
