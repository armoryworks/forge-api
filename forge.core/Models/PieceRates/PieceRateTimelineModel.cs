namespace Forge.Core.Models.PieceRates;

/// <summary>A scope's (part, optional operation) current rate + full history.</summary>
public record PieceRateTimelineModel(
    int PartId,
    string PartNumber,
    string? PartDescription,
    int? OperationId,
    PieceRateModel? Current,
    IReadOnlyList<PieceRateModel> History);
