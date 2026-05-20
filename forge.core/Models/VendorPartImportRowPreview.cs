namespace Forge.Core.Models;

/// <summary>
/// One row's worth of dry-run output from the VendorPart CSV import preview
/// endpoint. <see cref="PartId"/> / <see cref="PartName"/> are populated when
/// the PartNumber matched an existing part. <see cref="ErrorMessage"/> is set
/// only when <see cref="Action"/> is <see cref="BulkImportRowAction.Error"/>.
/// </summary>
public record VendorPartImportRowPreview(
    int LineNumber,
    string? PartNumber,
    string? PartName,
    int? PartId,
    string? VendorPartNumber,
    string? ManufacturerName,
    string? VendorMpn,
    int? LeadTimeDays,
    int? MinOrderQty,
    int? PackSize,
    string? CountryOfOrigin,
    string? HtsCode,
    string? Notes,
    BulkImportRowAction Action,
    string? ErrorMessage);
