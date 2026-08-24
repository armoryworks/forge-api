namespace Forge.Core.Models.PieceRates;

/// <summary>One row of a piece-rate timeline.</summary>
public record PieceRateModel(
    int Id,
    int PartId,
    string PartNumber,
    string? PartDescription,
    int? OperationId,
    decimal RatePerPiece,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Notes);
