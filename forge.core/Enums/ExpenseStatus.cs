namespace Forge.Core.Enums;

public enum ExpenseStatus
{
    Pending,
    Approved,
    Rejected,
    SelfApproved,
    NeedsRevision,
    // F-EXP-03: terminal state once an approved expense has been reimbursed (syncs to AP/QBO).
    Reimbursed
}
