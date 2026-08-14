namespace Forge.Core.Models;

/// <summary>A payout batch as reported by a platform, with its component lines.</summary>
public record ECommerceSettlement
{
    public string ExternalSettlementId { get; init; } = string.Empty;
    public DateTimeOffset PeriodStart { get; init; }
    public DateTimeOffset PeriodEnd { get; init; }
    public DateTimeOffset? DepositedAt { get; init; }

    /// <summary>Net amount the platform says it paid out. The reconciliation target.</summary>
    public decimal NetAmount { get; init; }

    public string CurrencyCode { get; init; } = "USD";
    public IReadOnlyList<ECommerceSettlementLine> Lines { get; init; } = [];
}
