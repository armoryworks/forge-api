namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a vendor's bank account under the BANK-002 controls: every create OR
/// change to the routing/account numbers starts at <see cref="PendingApproval"/> and
/// requires a SECOND user's approval (dual control — the change-maker can never
/// self-approve). A zero-dollar prenote then verifies the account with the receiving
/// bank before live dollars flow; <see cref="Verified"/> is the only state eligible
/// for payment batches when prenoting is required (the default).
/// </summary>
public enum VendorBankAccountStatus
{
    /// <summary>Created or edited — awaiting a second user's approval (dual control).</summary>
    PendingApproval,

    /// <summary>Approved by a distinct user; eligible for a prenote batch (or live batches when prenoting is disabled).</summary>
    Approved,

    /// <summary>Included in a released prenote batch — awaiting the return window to pass.</summary>
    PrenoteSent,

    /// <summary>Prenote window passed with no return — eligible for live payment batches.</summary>
    Verified,

    /// <summary>Disabled — excluded from all batches. Re-enabling requires a fresh dual-control approval.</summary>
    Disabled,
}
