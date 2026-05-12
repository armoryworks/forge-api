namespace Forge.Core.Models;

public record QcInspectionResultModel(
    int Id,
    int? ChecklistItemId,
    string Description,
    bool Passed,
    string? MeasuredValue,
    string? Notes);
