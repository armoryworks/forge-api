namespace Forge.Core.Entities.Accounting;

/// <summary>
/// QB-001 — maps one GL account to the QuickBooks Online account the CPA wants
/// its period net pushed to. One mapping per GL account (unique, soft-delete
/// filtered). Maintained by the controller on the exports screen; the push
/// refuses to run while any nonzero-net account is unmapped.
/// </summary>
public class QboAccountMap : BaseAuditableEntity
{
    public int GlAccountId { get; set; }

    /// <summary>QuickBooks Online Account.Id (their string ref, e.g. "79").</summary>
    public string QboAccountId { get; set; } = string.Empty;

    /// <summary>Display-only QBO account name snapshot (for the mapping table).</summary>
    public string? QboAccountName { get; set; }

    public GlAccount GlAccount { get; set; } = null!;
}
