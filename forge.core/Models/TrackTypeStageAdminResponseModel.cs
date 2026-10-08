namespace Forge.Core.Models;

public record TrackTypeStageAdminResponseModel(
    int Id,
    string Name,
    string Code,
    int SortOrder,
    string Color,
    int? WIPLimit,
    bool IsIrreversible,
    bool IsMandatory,
    string? AccountingDocumentType,
    bool IsActive,
    int OpenJobCount);
