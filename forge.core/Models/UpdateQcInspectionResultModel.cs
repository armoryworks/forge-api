namespace Forge.Core.Models;

public record UpdateQcInspectionResultModel(
    int? Id,
    int? ChecklistItemId,
    string Description,
    bool Passed,
    string? MeasuredValue,
    string? Notes);
