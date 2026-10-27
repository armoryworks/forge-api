using Forge.Core.Enums;

namespace Forge.Core.Models;

public record AdjustLeaveBalanceRequestModel(
    int PolicyId,
    LeaveAdjustmentType Type,
    decimal Hours,
    DateOnly EffectiveDate,
    string? Reason);
