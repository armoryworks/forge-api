using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Phase 3 H4 / WU-20 — list / summary view of one BOM revision.</summary>
public record BomRevisionSummaryResponseModel(
    int Id,
    int PartId,
    int RevisionNumber,
    DateTimeOffset EffectiveDate,
    string? Notes,
    int? CreatedByUserId,
    DateTimeOffset CreatedAt,
    int EntryCount,
    bool IsCurrent);

/// <summary>Phase 3 H4 / WU-20 — full revision detail incl. entries.</summary>
public record BomRevisionDetailResponseModel(
    int Id,
    int PartId,
    int RevisionNumber,
    DateTimeOffset EffectiveDate,
    string? Notes,
    int? CreatedByUserId,
    DateTimeOffset CreatedAt,
    bool IsCurrent,
    List<BomRevisionLineResponseModel> Entries);

public record BomRevisionLineResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string PartDescription,
    decimal Quantity,
    string UnitOfMeasure,
    int? OperationId,
    string? ReferenceDesignator,
    BOMSourceType SourceType,
    int? LeadTimeDays,
    string? Notes,
    int SortOrder);

/// <summary>
/// Phase 3 H4 / WU-20 — what BOM revision a job was released against.
/// </summary>
public record JobBomAtReleaseResponseModel(
    int JobId,
    int? PartId,
    int? BomRevisionId,
    int? RevisionNumber,
    DateTimeOffset? EffectiveDate,
    bool BomHasBeenUpdatedSinceRelease,
    int? CurrentRevisionId,
    int? CurrentRevisionNumber);
