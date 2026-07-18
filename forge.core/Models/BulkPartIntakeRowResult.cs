using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Per-row result. CreatedPartId / CreatedPartNumber are set on commit (the number is
/// server-issued, so the UI surfaces what was assigned); MatchedPartId points at the existing
/// part for a duplicate.</summary>
public record BulkPartIntakeRowResult(
    string? ExternalRowKey,
    BulkPartIntakeRowStatus Status,
    int? CreatedPartId,
    string? CreatedPartNumber,
    int? MatchedPartId,
    string? Message);
