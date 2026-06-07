using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A chart-of-accounts node. Control accounts (<see cref="IsControlAccount"/>)
/// post <b>only</b> via sub-ledgers (§5.1). Derives from <see cref="BaseEntity"/>
/// so it is exempt from the global soft-delete filter.
/// </summary>
public class GlAccount : BaseEntity
{
    public int BookId { get; set; }

    public string AccountNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public AccountType AccountType { get; set; }
    public NormalBalance NormalBalance { get; set; }

    /// <summary>Optional parent for roll-up / hierarchical CoA.</summary>
    public int? ParentAccountId { get; set; }

    /// <summary>True when this account is summarized by a sub-ledger.</summary>
    public bool IsControlAccount { get; set; }

    /// <summary>Which sub-ledger controls this account (set only when control).</summary>
    public ControlAccountType? ControlType { get; set; }

    /// <summary>False for summary/header accounts that cannot be posted to.</summary>
    public bool IsPostable { get; set; } = true;

    public bool IsActive { get; set; } = true;

    /// <summary>Dimension-required policy (§12): a line to this account must carry a Job (e.g. WIP/COGS).</summary>
    public bool RequiresJob { get; set; }

    /// <summary>Dimension-required policy (§12): a line to this account must carry a CostCenter (departmental).</summary>
    public bool RequiresCostCenter { get; set; }

    /// <summary>
    /// Optional cash-flow-statement classification (Operating / Investing / Financing). When null the
    /// statement uses a type-based heuristic. Tag long-term-asset / long-term-debt accounts here (Phase 4+).
    /// </summary>
    public CashFlowCategory? CashFlowCategory { get; set; }

    public string? Description { get; set; }

    public Book Book { get; set; } = null!;
    public GlAccount? ParentAccount { get; set; }
    public ICollection<GlAccount> ChildAccounts { get; set; } = [];
}
