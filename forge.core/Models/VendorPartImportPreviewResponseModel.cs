namespace Forge.Core.Models;

/// <summary>
/// Dry-run preview payload for the VendorPart CSV bulk import flow. Returned by
/// <c>POST /api/v1/vendors/{vendorId}/vendor-parts/import-preview</c>.
/// Pure read — no DB mutation.
/// </summary>
public record VendorPartImportPreviewResponseModel(
    int TotalRows,
    int AddCount,
    int UpdateCount,
    int ErrorCount,
    List<VendorPartImportRowPreview> Rows);
