namespace Forge.Core.Models;

/// <summary>
/// One open bill the payment run suggests paying: what to pay and the discount it earns if paid on the pay
/// date. <c>DaysLeft</c> counts days from the pay date to the discount deadline; null when no discount applies.
/// <c>VendorOpenCredit</c> is what the vendor's open credit memos in the bill's currency could deduct.
/// </summary>
public record VendorPaymentSuggestionModel(
    int VendorBillId,
    string BillNumber,
    int VendorId,
    string VendorName,
    string? VendorInvoiceNumber,
    int CurrencyId,
    string? CurrencyCode,
    DateTimeOffset BillDate,
    DateTimeOffset DueDate,
    decimal BalanceDue,
    decimal SuggestedPayAmount,
    decimal SuggestedDiscount,
    DateTimeOffset? DiscountDeadline,
    int? DaysLeft,
    string? PaymentTermsName,
    decimal VendorOpenCredit = 0m);
