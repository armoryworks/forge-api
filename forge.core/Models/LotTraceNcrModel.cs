namespace Forge.Core.Models;

public record LotTraceNcrModel(
    int Id,
    string NcrNumber,
    string Type,
    string Status,
    DateTimeOffset DetectedAt,
    string Description,
    decimal AffectedQuantity,
    string? DispositionCode);
