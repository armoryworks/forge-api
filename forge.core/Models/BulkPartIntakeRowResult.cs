using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Per-row result. CreatedPartNumber is the server-issued number: the one the row would
/// get on preview, the one assigned on commit, so the UI can show it either way. CreatedPartId is
/// set on commit only; MatchedPartId points at the existing part for a duplicate.</summary>
public record BulkPartIntakeRowResult(
    string? ExternalRowKey,
    BulkPartIntakeRowStatus Status,
    int? CreatedPartId,
    string? CreatedPartNumber,
    int? MatchedPartId,
    string? Message);
