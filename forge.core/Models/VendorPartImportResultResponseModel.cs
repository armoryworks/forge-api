namespace Forge.Core.Models;

/// <summary>
/// Summary payload for the VendorPart CSV bulk import apply endpoint. Returned
/// by <c>POST /api/v1/vendors/{vendorId}/vendor-parts/import-apply</c>. Per-row
/// results capture which VendorParts were added vs. updated vs. skipped.
/// </summary>
public record VendorPartImportResultResponseModel(
    int AddedCount,
    int UpdatedCount,
    int SkippedCount,
    int ErrorCount,
    List<VendorPartImportRowResult> Rows);
