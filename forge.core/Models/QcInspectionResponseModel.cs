namespace Forge.Core.Models;

public record QcInspectionResponseModel(
    int Id,
    int? JobId,
    string? JobNumber,
    int? ProductionRunId,
    int? TemplateId,
    string? TemplateName,
    int InspectorId,
    string InspectorName,
    string? LotNumber,
    string Status,
    string? Notes,
    DateTimeOffset? CompletedAt,
    List<QcInspectionResultModel> Results,
    DateTimeOffset CreatedAt);
