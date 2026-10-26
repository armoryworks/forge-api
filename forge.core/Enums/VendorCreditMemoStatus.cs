namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a vendor credit or debit memo. Draft memos can be edited or deleted; issuing posts them and opens them
/// for use. An open memo moves to PartiallyApplied and Applied as it is applied to bills, and Voided reverses the issue.
/// Stored as an integer.
/// </summary>
public enum VendorCreditMemoStatus
{
    Draft = 0,
    Open = 1,
    PartiallyApplied = 2,
    Applied = 3,
    Voided = 4,
}
