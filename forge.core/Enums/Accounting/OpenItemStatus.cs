namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Lifecycle of an AR/AP open item (<c>ArOpenItem</c> / <c>ApOpenItem</c>) — the per-document
/// sub-ledger row maintained at posting time alongside the control-account journal.
/// <list type="bullet">
///   <item><see cref="Open"/> — nothing applied yet; the full document amount is outstanding.</item>
///   <item><see cref="PartiallyApplied"/> — some, but not all, of the document has been settled.</item>
///   <item><see cref="Closed"/> — applications cover the full original amount (fully settled).</item>
///   <item><see cref="Voided"/> — the source document was voided and its origination journal
///         reversed. A Voided item is excluded from aging AND from the sub-ledger side of the
///         control-vs-open-items reconciliation — matching the reversed GL, which nets the
///         control account to zero for that document (it contributes to neither side).</item>
/// </list>
/// </summary>
public enum OpenItemStatus
{
    Open,
    PartiallyApplied,
    Closed,
    Voided,
}
