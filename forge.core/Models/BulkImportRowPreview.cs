namespace Forge.Core.Models;

/// <summary>
/// One row's worth of dry-run output from the price-list-entry CSV import
/// preview endpoint. Lookups (PartId / PartName) are populated when the
/// PartNumber matched an existing part. <see cref="ErrorMessage"/> is set
/// only when <see cref="Action"/> is <see cref="BulkImportRowAction.Error"/>.
/// </summary>
public record BulkImportRowPreview(
    int LineNumber,
    string? PartNumber,
    string? PartName,
    int? PartId,
    decimal? UnitPrice,
    int MinQuantity,
    string Currency,
    string? Notes,
    BulkImportRowAction Action,
    string? ErrorMessage);
