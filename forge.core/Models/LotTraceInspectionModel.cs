namespace Forge.Core.Models;

public record LotTraceInspectionModel(
    int Id,
    string Status,
    string InspectorName,
    DateTimeOffset CreatedAt);
