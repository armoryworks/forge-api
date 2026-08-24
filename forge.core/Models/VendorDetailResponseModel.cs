namespace Forge.Core.Models;

public record VendorDetailResponseModel(
    int Id,
    string CompanyName,
    string? VendorNumber,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    string? PaymentTerms,
    string? Notes,
    decimal? OffTierVariancePct,
    bool IsActive,
    bool Is1099,
    string? TaxId,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<PurchaseOrderListItemModel> PurchaseOrders);
