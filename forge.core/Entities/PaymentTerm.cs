using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A payment-terms master row: net days to the due date and an optional early-payment discount
/// ("2/10 Net 30" = 2 % off when paid within 10 days, otherwise due in 30). Customers, vendors, orders,
/// invoices and bills point at one. System rows are seeded and can only be renamed.
/// </summary>
public class PaymentTerm : BaseAuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Days from the document date to the due date.</summary>
    public int NetDays { get; set; }

    /// <summary>Early-payment discount in percent (2 = 2 %). 0 = no discount.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Days from the document date during which the discount may be taken.</summary>
    public int DiscountDays { get; set; }

    /// <summary>The <see cref="CreditTerms"/> value these terms stand for, written back to the legacy credit-terms columns.</summary>
    public CreditTerms? LegacyCreditTerms { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Seeded row: name editable only, never deactivated.</summary>
    public bool IsSystem { get; set; }

    public bool HasDiscount => DiscountPercent > 0m && DiscountDays > 0;
}
