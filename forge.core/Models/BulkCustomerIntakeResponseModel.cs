namespace Forge.Core.Models;

/// <summary>C2: customer bulk-intake summary + per-row results.</summary>
public record BulkCustomerIntakeResponseModel(
    int TotalRows,
    int CreatedCount,
    int SkippedCount,
    List<BulkCustomerIntakeRowResult> Results);
