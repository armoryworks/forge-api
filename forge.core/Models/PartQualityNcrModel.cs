namespace Forge.Core.Models;

public record PartQualityNcrModel(
    int Id,
    string NcrNumber,
    string Type,
    string Status,
    string? LotNumber,
    DateTimeOffset DetectedAt,
    string Description,
    decimal AffectedQuantity);
