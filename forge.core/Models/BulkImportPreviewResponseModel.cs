namespace Forge.Core.Models;

/// <summary>
/// Dry-run preview payload for the PriceListEntry CSV bulk import flow.
/// Returned by <c>POST /api/v1/price-lists/{id}/entries/import-preview</c>.
/// Pure read — no DB mutation.
/// </summary>
public record BulkImportPreviewResponseModel(
    int TotalRows,
    int AddCount,
    int UpdateCount,
    int ErrorCount,
    List<BulkImportRowPreview> Rows);
