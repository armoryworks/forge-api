namespace Forge.Core.Models;

public record LeaveBalanceResponseModel(
    int Id,
    int UserId,
    int PolicyId,
    string PolicyName,
    decimal Balance,
    decimal UsedThisYear,
    decimal AccruedThisYear,
    DateTimeOffset LastAccrualDate);
