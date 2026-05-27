namespace Forge.Core.Models;

/// <summary>C2: customer bulk-intake payload (same shape for preview + commit).</summary>
public record BulkCustomerIntakeRequest(List<BulkCustomerIntakeRow>? Rows);
