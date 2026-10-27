namespace Forge.Core.Models;

/// <summary>
/// One CSV row of a job import. Customer and part are matched by number, then by name. Stage and track
/// type are matched by name or code; blank picks the default track type and its first stage. A blank job
/// number draws the next number.
/// </summary>
public record JobImportRowModel(
    string? RowKey,
    string? JobNumber,
    string? Title,
    string? Customer,
    string? Part,
    decimal? Quantity,
    DateTimeOffset? StartDate,
    DateTimeOffset? DueDate,
    DateTimeOffset? CompletedDate,
    string? Stage,
    string? TrackType,
    string? Priority,
    string? CustomerPO,
    string? Description);
