namespace Forge.Core.Models;

/// <summary>Part bulk-intake payload (same shape for preview + commit).</summary>
public record BulkPartIntakeRequest(List<BulkPartIntakeRow>? Rows);
