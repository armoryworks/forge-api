using System.Security.Claims;
using System.Text.Json;

using Forge.Core.Entities;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>
/// Decides the early-payment discount an AR or AP application takes. A null request takes the automatic
/// discount when the payment date is in the window; an explicit amount must be in the window and within the
/// remaining maximum, unless an Admin or Manager asks for an override (audited as <c>PaymentDiscountOverride</c>).
/// </summary>
public static class EarlyPaymentDiscountRules
{
    public const string NotAllowedCode = "discount-not-allowed";
    public const string OverrideAuditAction = "PaymentDiscountOverride";

    public static decimal InvoiceBase(Invoice invoice, DiscountSettings settings)
        => settings.BaseIsSubtotal ? Money.Round(invoice.Subtotal) : Money.Round(invoice.Total);

    public static decimal BillBase(VendorBill bill, DiscountSettings settings)
        => settings.BaseIsSubtotal ? Money.Round(bill.Subtotal) : bill.Total;

    /// <summary>The discount still available on an invoice, ignoring the date.</summary>
    public static decimal RemainingMaximum(Invoice invoice, DiscountSettings settings)
        => Math.Max(0m, EarlyPaymentDiscount.Maximum(InvoiceBase(invoice, settings), invoice.DiscountPercent) - invoice.DiscountsTaken);

    /// <summary>The discount still available on a bill, ignoring the date.</summary>
    public static decimal RemainingMaximum(VendorBill bill, DiscountSettings settings)
        => Math.Max(0m, EarlyPaymentDiscount.Maximum(BillBase(bill, settings), bill.DiscountPercent) - bill.DiscountsTaken);

    /// <summary>The discount available to a payment dated <paramref name="paymentDate"/>: the remaining maximum in the window, else 0.</summary>
    public static decimal AvailableOn(Invoice invoice, DateTimeOffset paymentDate, DiscountSettings settings)
        => invoice.BalanceDue > 0m && EarlyPaymentDiscount.InWindow(invoice.DiscountDueDate, paymentDate, settings.GraceDays)
            ? Math.Min(RemainingMaximum(invoice, settings), invoice.BalanceDue)
            : 0m;

    /// <summary>The discount available to a payment dated <paramref name="paymentDate"/>: the remaining maximum in the window, else 0.</summary>
    public static decimal AvailableOn(VendorBill bill, DateTimeOffset paymentDate, DiscountSettings settings)
        => bill.BalanceDue > 0m && EarlyPaymentDiscount.InWindow(bill.DiscountDueDate, paymentDate, settings.GraceDays)
            ? Math.Min(RemainingMaximum(bill, settings), bill.BalanceDue)
            : 0m;

    public static decimal ForInvoice(
        AppDbContext db, Invoice invoice, decimal cash, DateTimeOffset paymentDate, decimal? requested,
        bool allowOverride, ClaimsPrincipal? user, DiscountSettings settings, string field)
        => Resolve(db, "Invoice", invoice.Id, invoice.InvoiceNumber, cash, invoice.BalanceDue,
            RemainingMaximum(invoice, settings), invoice.DiscountPercent, invoice.DiscountDueDate,
            paymentDate, requested, allowOverride, user, settings, field);

    public static decimal ForBill(
        AppDbContext db, VendorBill bill, decimal cash, DateTimeOffset paymentDate, decimal? requested,
        bool allowOverride, ClaimsPrincipal? user, DiscountSettings settings, string field)
        => Resolve(db, "VendorBill", bill.Id, bill.BillNumber, cash, bill.BalanceDue,
            RemainingMaximum(bill, settings), bill.DiscountPercent, bill.DiscountDueDate,
            paymentDate, requested, allowOverride, user, settings, field);

    private static decimal Resolve(
        AppDbContext db, string documentType, int documentId, string documentNumber,
        decimal cash, decimal balanceDue, decimal remainingMaximum, decimal discountPercent,
        DateTimeOffset? discountDueDate, DateTimeOffset paymentDate, decimal? requested,
        bool allowOverride, ClaimsPrincipal? user, DiscountSettings settings, string field)
    {
        var inWindow = EarlyPaymentDiscount.InWindow(discountDueDate, paymentDate, settings.GraceDays);

        if (requested is null)
            return inWindow ? EarlyPaymentDiscount.Automatic(cash, balanceDue, remainingMaximum, discountPercent) : 0m;

        var amount = requested.Value;
        if (amount == 0m)
            return 0m;

        if (inWindow && amount <= remainingMaximum)
            return amount;

        if (allowOverride && (user?.IsInRole("Admin") == true || user?.IsInRole("Manager") == true))
        {
            db.AuditLogEntries.Add(new AuditLogEntry
            {
                UserId = db.CurrentUserId ?? 0,
                Action = OverrideAuditAction,
                EntityType = documentType,
                EntityId = documentId,
                Details = JsonSerializer.Serialize(new
                {
                    document = documentNumber,
                    cash,
                    discount = amount,
                    allowed = inWindow ? remainingMaximum : 0m,
                    discountDueDate,
                    paymentDate,
                }),
            });
            return amount;
        }

        var reason = inWindow
            ? $"A discount of {amount:0.00} on {documentNumber} is more than the {remainingMaximum:0.00} its terms allow."
            : discountDueDate is null
                ? $"{documentNumber} has no early-payment discount."
                : $"The early-payment discount on {documentNumber} ended on {EarlyPaymentDiscount.Deadline(discountDueDate, settings.GraceDays):MM/dd/yyyy}.";
        throw PaymentTermsResolver.Failure(field, NotAllowedCode, reason);
    }
}
