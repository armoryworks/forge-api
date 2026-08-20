namespace Forge.Core.Models;

public record UpdateSalesOrderRequestModel(
    int? ShippingAddressId,
    int? BillingAddressId,
    string? CreditTerms,
    DateTimeOffset? RequestedDeliveryDate,
    string? CustomerPO,
    string? Notes,
    decimal? TaxRate,
    // Optional editable order number — changeable only while the order is Draft
    // and only when the sales_orders.allow_manual_numbers setting is on.
    string? OrderNumber = null);
