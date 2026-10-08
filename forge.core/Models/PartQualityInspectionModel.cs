namespace Forge.Core.Models;

public record PartQualityInspectionModel(
    int Id,
    string Status,
    string? TemplateName,
    string? JobNumber,
    string? LotNumber,
    string InspectorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int PassedCount,
    int FailedCount);
