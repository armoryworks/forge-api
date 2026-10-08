namespace Forge.Core.Models;

public record QcInspectionResultModel(
    int Id,
    int? ChecklistItemId,
    string Description,
    string? Specification,
    bool IsRequired,
    bool Passed,
    string? MeasuredValue,
    string? Notes);
