namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Links a payment to a specific invoice. Enables partial payments and overpayments.
/// </summary>
public class PaymentApplication : BaseEntity
{
    public int PaymentId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// FX rate (transaction→functional) in effect when this payment settled the invoice. Default 1
    /// (single-currency). The cash-receipt posting relieves AR at the invoice's BOOKING rate and brings
    /// cash in at this SETTLEMENT rate, plugging the difference to realized FX gain/loss.
    /// </summary>
    public decimal SettlementFxRate { get; set; } = 1m;

    /// <summary>
    /// Early-payment discount taken with this application, in the invoice's currency. It settles the invoice together
    /// with <see cref="Amount"/> (the cash) and posts to the discounts account at the settlement rate.
    /// </summary>
    public decimal DiscountAmount { get; set; }

    public Payment Payment { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
}
