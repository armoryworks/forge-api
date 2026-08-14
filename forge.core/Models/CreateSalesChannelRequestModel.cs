using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateSalesChannelRequestModel
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public SalesChannelType ChannelType { get; init; } = SalesChannelType.DirectB2B;

    /// <summary>Required for retail and marketplace channels — the house account that carries the receivable.</summary>
    public int? SoldToCustomerId { get; init; }

    /// <summary>Omit to take the type's natural default: Marketplace channels default to marketplace-collected tax, everything else to seller-collected.</summary>
    public TaxCollectedBy? TaxCollectedBy { get; init; }

    public string? OrderNumberPrefix { get; init; }
    public int? ECommerceIntegrationId { get; init; }
}
