namespace Forge.Core.Models;

public record CreateQuoteRequestModel(
    int CustomerId,
    int? ShippingAddressId,
    DateTimeOffset? ExpirationDate,
    string? Notes,
    decimal TaxRate,
    List<CreateQuoteLineModel> Lines,
    string? CustomerPO = null,
    // Optional caller-supplied quote number — honored only when the
    // quotes.allow_manual_numbers setting is on; otherwise auto-generated.
    string? QuoteNumber = null);

public record CreateQuoteLineModel(
    int? PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes);
