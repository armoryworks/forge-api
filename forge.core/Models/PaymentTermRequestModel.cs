namespace Forge.Core.Models;

public record PaymentTermRequestModel(
    string Code,
    string Name,
    int NetDays,
    decimal DiscountPercent,
    int DiscountDays,
    bool IsActive = true,
    int SortOrder = 0);
