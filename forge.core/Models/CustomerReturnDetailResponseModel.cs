namespace Forge.Core.Models;

public record CustomerReturnDetailResponseModel(
    int Id,
    string ReturnNumber,
    int CustomerId,
    string CustomerName,
    /// <summary>Null on retail returns of stocked items, which have no originating job.</summary>
    int? OriginalJobId,
    string? OriginalJobNumber,
    string? OriginalJobTitle,
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
    DateTimeOffset UpdatedAt,
    /// <summary>Set when the return was authorised on a sales channel rather than in Forge.</summary>
    int? ChannelId = null,
    string? ChannelName = null,
    string? ExternalRmaId = null,
    decimal? RefundAmount = null,
    decimal? Quantity = null,
    int? SalesOrderLineId = null);
