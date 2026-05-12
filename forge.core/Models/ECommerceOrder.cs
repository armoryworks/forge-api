namespace Forge.Core.Models;

public record ECommerceOrder
{
    public string ExternalId { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public IReadOnlyList<ECommerceOrderLine> Lines { get; init; } = [];
    public ECommerceAddress ShippingAddress { get; init; } = new();
    public decimal TotalAmount { get; init; }
    public string CurrencyCode { get; init; } = "USD";
    public DateTimeOffset OrderDate { get; init; }
}
