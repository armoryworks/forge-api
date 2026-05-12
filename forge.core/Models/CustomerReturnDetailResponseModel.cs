namespace Forge.Core.Models;

public record CustomerReturnDetailResponseModel(
    int Id,
    string ReturnNumber,
    int CustomerId,
    string CustomerName,
    int OriginalJobId,
    string OriginalJobNumber,
    string OriginalJobTitle,
    int? ReworkJobId,
    string? ReworkJobNumber,
    string Status,
    string Reason,
    string? Notes,
    DateTimeOffset ReturnDate,
    int? InspectedById,
    string? InspectedByName,
    DateTimeOffset? InspectedAt,
    string? InspectionNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
