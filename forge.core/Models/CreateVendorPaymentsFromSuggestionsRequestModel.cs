namespace Forge.Core.Models;

/// <summary>
/// The bills picked from the payment-run suggestions. One vendor payment is created per vendor and currency,
/// each dated <c>PaymentDate</c> and paid by <c>Method</c>.
/// </summary>
public record CreateVendorPaymentsFromSuggestionsRequestModel(
    DateTimeOffset PaymentDate,
    string Method,
    string? ReferenceNumber,
    string? Notes,
    List<VendorPaymentSuggestionSelectionModel> Items);

/// <summary>A picked bill: the cash to pay and the discount to take (null = the automatic discount).</summary>
public record VendorPaymentSuggestionSelectionModel(int VendorBillId, decimal Amount, decimal? DiscountAmount = null);
