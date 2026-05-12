namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Links a payment to a specific invoice. Enables partial payments and overpayments.
/// </summary>
public class PaymentApplication : BaseEntity
{
    public int PaymentId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }

    public Payment Payment { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
}
