namespace Forge.Core.Enums.Accounting;

/// <summary>
/// GL segregation-of-duties capability keys (ACCOUNTING_SUITE_PLAN §5.7).
/// These are <b>operation-level</b> authorization keys enforced at the
/// <see cref="Forge.Core.Interfaces.IPostingEngine"/> boundary — they are NOT
/// the same thing as the install-level <c>CAP-ACCT-*</c> feature flags in the
/// capability catalog (which gate whether the GL module exists at all). A
/// caller must hold the relevant GL capability in their <b>effective</b>
/// (resolved) permission set to drive the corresponding GL operation.
/// <para>
/// Per §5.7 the default grants attach to <c>Controller</c> (and any rollup that
/// composes it — e.g. the seeded <c>OwnerOperator</c> and back-office
/// templates). Authorization is evaluated against the effective set, never
/// against hard-coded role names — see <c>ICurrentUserCapabilities</c>.
/// </para>
/// </summary>
public enum GlCapability
{
    /// <summary>Post a manual JE / sub-ledger entry. Default grant: Controller.</summary>
    PostJournalEntry,

    /// <summary>Approve a JE in the maker-checker flow. Default grant: Controller.</summary>
    ApproveJournalEntry,

    /// <summary>Reverse a posted entry. Default grant: Controller.</summary>
    ReverseJournalEntry,

    /// <summary>Soft-close a fiscal period. Default grant: Controller (+ bookkeeping role if configured).</summary>
    ClosePeriodSoft,

    /// <summary>Hard-close a fiscal period. Default grant: Controller.</summary>
    ClosePeriodHard,

    /// <summary>Re-open a closed period. Default grant: Controller.</summary>
    ReopenPeriod,

    /// <summary>Configure the GL (CoA / determination rules). Default grant: Controller.</summary>
    ConfigureGl,
}
