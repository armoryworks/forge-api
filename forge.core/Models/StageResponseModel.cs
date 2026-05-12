namespace Forge.Core.Models;

public record StageResponseModel(
    int Id,
    string Name,
    string Code,
    int SortOrder,
    string Color,
    int? WIPLimit,
    string? AccountingDocumentType,
    bool IsIrreversible);
