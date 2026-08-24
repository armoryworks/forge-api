namespace Forge.Core.Models.PieceRates;

/// <summary>The weekly minimum-wage make-up report over piece workers.</summary>
public record PieceRateComplianceModel(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<PieceRateComplianceRowModel> Rows,
    decimal TotalMakeupOwed);
