namespace Forge.Core.Models.PieceRates;

/// <summary>Pieces a worker completed on a date, at the rate in force that day.</summary>
public record PieceWorkEntryModel(
    int Id,
    int UserId,
    string UserName,
    int PartId,
    string PartNumber,
    int? OperationId,
    DateOnly WorkDate,
    decimal Quantity,
    decimal RateSnapshot,
    decimal Earnings,
    string? Notes);
