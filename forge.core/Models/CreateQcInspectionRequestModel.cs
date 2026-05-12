namespace Forge.Core.Models;

public record CreateQcInspectionRequestModel(
    int? JobId,
    int? ProductionRunId,
    int? TemplateId,
    string? LotNumber,
    string? Notes);
