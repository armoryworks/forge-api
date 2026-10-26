namespace Forge.Core.Models;

public record PaymentTermResponseModel(
    int Id,
    string Code,
    string Name,
    int NetDays,
    decimal DiscountPercent,
    int DiscountDays,
    string? LegacyCreditTerms,
    bool IsActive,
    int SortOrder,
    bool IsSystem);
