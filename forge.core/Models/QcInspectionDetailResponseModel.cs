namespace Forge.Core.Models;

public record QcInspectionDetailResponseModel(
    int Id,
    int? JobId,
    string? JobNumber,
    string? JobTitle,
    int? ProductionRunId,
    string? ProductionRunNumber,
    int? TemplateId,
    string? TemplateName,
    int? PartId,
    string? PartNumber,
    string? PartName,
    int InspectorId,
    string InspectorName,
    string? LotNumber,
    string Status,
    string? Notes,
    DateTimeOffset? CompletedAt,
    List<QcInspectionResultModel> Results,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
