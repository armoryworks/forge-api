using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SalesChannelResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public SalesChannelType ChannelType { get; init; }
    public int? SoldToCustomerId { get; init; }
    public string? SoldToCustomerName { get; init; }
    public TaxCollectedBy TaxCollectedBy { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public string? OrderNumberPrefix { get; init; }
    public int? ECommerceIntegrationId { get; init; }

    /// <summary>True for DirectRetail and Marketplace — the UI uses this to decide whether to show buyer/settlement surfaces.</summary>
    public bool IsRetail { get; init; }

    public int OrderCount { get; init; }
    public int ListingCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
