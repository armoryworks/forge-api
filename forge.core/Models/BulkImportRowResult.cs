namespace Forge.Core.Models;

/// <summary>
/// Per-row outcome from the price-list-entry CSV import apply endpoint.
/// <see cref="CreatedOrUpdatedEntryId"/> is populated when the row resulted in
/// a new or updated <c>PriceListEntry</c>; <see cref="ErrorMessage"/> is set
/// when the row was skipped or errored.
/// </summary>
public record BulkImportRowResult(
    int LineNumber,
    BulkImportRowAction Action,
    int? CreatedOrUpdatedEntryId,
    string? ErrorMessage);
