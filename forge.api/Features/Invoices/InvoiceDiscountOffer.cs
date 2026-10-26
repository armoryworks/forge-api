using Forge.Api.Features.PaymentTerms;
using Forge.Core.Entities;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// The terms line an invoice document prints: the terms' name and, while the early-payment discount is still open,
/// what to pay and by when to take it ("Pay $9,604.00 by 11/05/2026 to take the discount").
/// </summary>
public sealed record InvoiceDiscountOffer(string? TermsName, decimal PayAmount, decimal Discount, DateTimeOffset? Deadline)
{
    public static readonly InvoiceDiscountOffer None = new(null, 0m, 0m, null);

    public bool HasDiscount => Discount > 0m && Deadline is not null;

    /// <summary>The offer as of <paramref name="asOf"/>; with no date the discount is offered until its deadline regardless.</summary>
    public static async Task<InvoiceDiscountOffer> ForAsync(AppDbContext db, Invoice invoice, DateTimeOffset? asOf, CancellationToken ct)
    {
        var terms = await new PaymentTermsResolver(db).EffectiveAsync(invoice.PaymentTermsId, invoice.CreditTerms, ct);
        var settings = await DiscountSettings.ReadAsync(db, ct);
        var deadline = EarlyPaymentDiscount.Deadline(invoice.DiscountDueDate, settings.GraceDays);
        var open = deadline is not null
            && (asOf is null || EarlyPaymentDiscount.InWindow(invoice.DiscountDueDate, asOf.Value, settings.GraceDays));
        var discount = open && invoice.BalanceDue > 0m ? EarlyPaymentDiscountRules.RemainingMaximum(invoice, settings) : 0m;
        return new InvoiceDiscountOffer(terms?.Name, Math.Max(0m, invoice.BalanceDue - discount), discount, discount > 0m ? deadline : null);
    }
}
