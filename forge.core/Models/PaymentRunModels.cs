namespace Forge.Core.Models;

/// <summary>
/// A payment run: the bills picked to pay on <c>PaymentDate</c> by <c>Method</c>. With <c>ApplyVendorCredits</c> each
/// vendor's open credit memos in the bill's currency are deducted from its payment, oldest first.
/// </summary>
public record CreatePaymentRunRequestModel(
    DateTimeOffset PaymentDate,
    string Method,
    List<VendorPaymentSuggestionSelectionModel> Items,
    string? ReferenceNumber = null,
    string? Notes = null,
    bool ApplyVendorCredits = true);

public record PaymentRunListItemModel(
    int Id,
    string RunNumber,
    string Status,
    DateTimeOffset PaymentDate,
    string Method,
    int ItemCount,
    int PaidCount,
    int FailedCount,
    int PendingCount,
    decimal CashTotal,
    decimal CreditTotal,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public record PaymentRunDetailResponseModel(
    int Id,
    string RunNumber,
    string Status,
    DateTimeOffset PaymentDate,
    string Method,
    string? ReferenceNumber,
    string? Notes,
    bool ApplyVendorCredits,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    List<PaymentRunItemModel> Items);

public record PaymentRunItemModel(
    int Id,
    int VendorBillId,
    string? BillNumber,
    int VendorId,
    string VendorName,
    int CurrencyId,
    string? CurrencyCode,
    decimal Amount,
    decimal? DiscountAmount,
    decimal CreditAmount,
    decimal CashAmount,
    string Status,
    int? VendorPaymentId,
    string? PaymentNumber,
    string? ErrorMessage,
    DateTimeOffset? ProcessedAt);
