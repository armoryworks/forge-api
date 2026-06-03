namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Identifies which sub-ledger a control account summarizes. Control accounts
/// (§5.1) post <b>only</b> via their sub-ledger; nullable on <c>GlAccount</c>
/// (set only when <c>IsControlAccount</c> is true).
/// </summary>
public enum ControlAccountType
{
    AR,
    AP,
    Inventory,
}
