namespace Forge.Core.Models.PieceRates;

/// <summary>One worker-week of the make-up check: piece earnings vs hours × minimum wage.</summary>
public record PieceRateComplianceRowModel(
    int UserId,
    string UserName,
    string? StateCode,
    decimal MinimumWage,
    decimal HoursWorked,
    decimal PieceEarnings,
    decimal RequiredFloor,
    decimal MakeupOwed,
    decimal EffectiveHourly);
