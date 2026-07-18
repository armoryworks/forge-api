namespace Forge.Core.Models;

/// <summary>Part bulk-intake summary + per-row results.</summary>
public record BulkPartIntakeResponseModel(
    int TotalRows,
    int CreatedCount,
    int SkippedCount,
    List<BulkPartIntakeRowResult> Results);
