namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — one entry line of a payment batch (masked account display only).</summary>
public record PaymentBatchItemModel(
    int Id,
    int? VendorPaymentId,
    string? PaymentNumber,
    int VendorId,
    string VendorName,
    string AccountNumberMasked,
    decimal Amount,
    string? TraceNumber);
