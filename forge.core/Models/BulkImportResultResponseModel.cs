namespace Forge.Core.Models;

/// <summary>
/// Summary payload for the PriceListEntry CSV bulk import apply endpoint.
/// Returned by <c>POST /api/v1/price-lists/{id}/entries/import-apply</c>.
/// Per-row results capture which entries were added vs. updated vs. skipped.
/// </summary>
public record BulkImportResultResponseModel(
    int AddedCount,
    int UpdatedCount,
    int SkippedCount,
    int ErrorCount,
    List<BulkImportRowResult> Rows);
