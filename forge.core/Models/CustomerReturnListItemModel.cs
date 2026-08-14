namespace Forge.Core.Models;

public record CustomerReturnListItemModel(
    int Id,
    string ReturnNumber,
    int CustomerId,
    string CustomerName,
    /// <summary>Null on retail returns of stocked items, which have no originating job.</summary>
    int? OriginalJobId,
    string? OriginalJobNumber,
    int? ReworkJobId,
    string? ReworkJobNumber,
    string Status,
    string Reason,
    DateTimeOffset ReturnDate,
    DateTimeOffset CreatedAt);
