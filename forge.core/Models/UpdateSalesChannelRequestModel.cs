using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateSalesChannelRequestModel
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? SoldToCustomerId { get; init; }
    public TaxCollectedBy? TaxCollectedBy { get; init; }
    public string? OrderNumberPrefix { get; init; }
    public int? ECommerceIntegrationId { get; init; }
    public bool? IsActive { get; init; }
}
