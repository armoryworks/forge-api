namespace Forge.Core.Models;

/// <summary>
/// One row's outcome in a CSV import. <see cref="Status"/> is <c>Valid</c> (dry run, would import),
/// <c>Created</c> (committed) or <c>Invalid</c> (see <see cref="Errors"/>). <see cref="Number"/> is the
/// document number the row has or would get; <see cref="CreatedId"/> is set once committed.
/// </summary>
public record DataImportRowResultModel(
    string? RowKey,
    int RowNumber,
    string Status,
    List<string> Errors,
    string? Number,
    int? CreatedId);
