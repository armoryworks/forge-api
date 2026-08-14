namespace Forge.Core.Models;

public record RetailBuyerResponseModel
{
    public int Id { get; init; }
    public int ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string ExternalBuyerId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? ContactEmail { get; init; }
    public string? Phone { get; init; }
    public bool MarketingConsent { get; init; }
    public DateTimeOffset? FirstOrderAt { get; init; }
    public DateTimeOffset? LastOrderAt { get; init; }
    public int OrderCount { get; init; }
    public DateTimeOffset? PurgeAfter { get; init; }

    /// <summary>Set once the PII columns have been scrubbed. A purged buyer keeps its order history but no longer identifies anyone.</summary>
    public DateTimeOffset? PurgedAt { get; init; }

    public decimal LifetimeValue { get; init; }
}
