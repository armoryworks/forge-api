namespace Forge.Core.Models;

/// <summary>
/// Per-row outcome from the VendorPart CSV import apply endpoint.
/// <see cref="VendorPartId"/> is populated when the row resulted in a new or
/// updated VendorPart; <see cref="ErrorMessage"/> is set when the row was
/// skipped or errored.
/// </summary>
public record VendorPartImportRowResult(
    int LineNumber,
    BulkImportRowAction Action,
    int? VendorPartId,
    string? ErrorMessage);
