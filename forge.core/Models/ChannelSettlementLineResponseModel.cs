using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ChannelSettlementLineResponseModel
{
    public int Id { get; init; }
    public ChannelSettlementLineType LineType { get; init; }
    public int? SalesOrderId { get; init; }
    public string? SalesOrderNumber { get; init; }
    public string? ExternalOrderId { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>True when the line names an external order we could not resolve — needs a human.</summary>
    public bool IsUnmatched { get; init; }
}
