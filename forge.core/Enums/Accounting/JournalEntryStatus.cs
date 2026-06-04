namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Lifecycle of a <c>JournalEntry</c>. Only <see cref="Posted"/> is append-only
/// (immutable); the sole mutation permitted on a Posted row is the single
/// <see cref="Posted"/>→<see cref="Reversed"/> flip written by reversal (§5.2).
/// Draft/PendingApproval/Approved rows are mutable by their author until posted.
/// </summary>
public enum JournalEntryStatus
{
    Draft,
    PendingApproval,
    Approved,
    Posted,
    Reversed,
}
