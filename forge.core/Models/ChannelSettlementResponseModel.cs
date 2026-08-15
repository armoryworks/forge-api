using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ChannelSettlementResponseModel
{
    public int Id { get; init; }
    public int ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string ExternalSettlementId { get; init; } = string.Empty;
    public DateTimeOffset PeriodStart { get; init; }
    public DateTimeOffset PeriodEnd { get; init; }
    public DateTimeOffset? DepositedAt { get; init; }
    public decimal ReportedNetAmount { get; init; }

    /// <summary>Sum of the imported components. Equals the reported net when the batch ties out.</summary>
    public decimal ComputedNetAmount { get; init; }

    /// <summary>Signed difference between what the channel says it paid and what its own detail adds up to.</summary>
    public decimal Variance { get; init; }

    public string CurrencyCode { get; init; } = "USD";
    public ChannelSettlementStatus Status { get; init; }
    public string? ResolutionNotes { get; init; }
    public int LineCount { get; init; }

    /// <summary>Order-linked components whose order could not be resolved — the reconciliation exceptions.</summary>
    public int UnmatchedLineCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
