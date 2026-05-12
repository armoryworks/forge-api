namespace Forge.Core.Models;

public record CustomerReturnListItemModel(
    int Id,
    string ReturnNumber,
    int CustomerId,
    string CustomerName,
    int OriginalJobId,
    string OriginalJobNumber,
    int? ReworkJobId,
    string? ReworkJobNumber,
    string Status,
    string Reason,
    DateTimeOffset ReturnDate,
    DateTimeOffset CreatedAt);
