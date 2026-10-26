namespace Forge.Core.Models;

/// <summary>The terms a document or party carries, as read models return them beside the legacy credit terms.</summary>
public record PaymentTermsModel(
    int Id,
    string Code,
    string Name,
    int NetDays,
    decimal DiscountPercent,
    int DiscountDays);
