namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a credit memo. Draft memos can be edited or deleted; issuing posts them. An issued memo moves to
/// PartiallyApplied, Applied (fully used against invoices) or Refunded (its rest paid back) as it is used, and
/// Voided reverses the issue.
/// </summary>
public enum CreditMemoStatus
{
    Draft = 0,
    Issued = 1,
    PartiallyApplied = 2,
    Applied = 3,
    Refunded = 4,
    Voided = 5,
}
