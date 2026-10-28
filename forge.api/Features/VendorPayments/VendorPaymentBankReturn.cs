namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// A bank's ACH return (R-code) of a transmitted vendor payment: the code and its description, the original
/// trace number, and the return date the reversing journal is dated.
/// </summary>
public sealed record VendorPaymentBankReturn(string ReasonCode, string Description, string TraceNumber, DateOnly ReturnDate)
{
    public string Reason => $"{ReasonCode} — {Description}";
}
