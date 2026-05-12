namespace Forge.Core.Models;

public record CreateSalesOrderRequestModel(
    int CustomerId,
    int? QuoteId,
    int? ShippingAddressId,
    int? BillingAddressId,
    string? CreditTerms,
    DateTimeOffset? RequestedDeliveryDate,
    string? CustomerPO,
    string? Notes,
    decimal TaxRate,
    List<CreateSalesOrderLineModel> Lines);

// Phase 3 / WU-10 / F8-partial — Quantity is decimal (was int). UoM-aware shops
// need fractional quantities — material-by-weight, by-time, by-volume.
public record CreateSalesOrderLineModel(
    int? PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes);
