using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// The balanced double-entry posting request handed to
/// <see cref="Forge.Core.Interfaces.IPostingEngine.PostAsync"/>. Built by the
/// operational command handler (e.g. <c>ReceiveItems</c>, <c>CreateInvoice</c>)
/// at the command site — handlers never touch <c>JournalEntry</c> directly
/// (§5.2, §7). A request is validated, then written as an immutable
/// <c>JournalEntry</c> inside the caller's transaction.
/// </summary>
public sealed class PostingRequest
{
    public int BookId { get; init; }

    /// <summary>
    /// Resolves the fiscal period in the book's ReportingTimeZone; immune to
    /// UTC normalization (<see cref="DateOnly"/>).
    /// </summary>
    public DateOnly EntryDate { get; init; }

    public JournalSource Source { get; init; }

    /// <summary>Polymorphic source link — type half (e.g. "Invoice").</summary>
    public string? SourceType { get; init; }

    /// <summary>Polymorphic source link — id half.</summary>
    public long? SourceId { get; init; }

    /// <summary>The entry currency. Phase-0 single-currency: every line shares it.</summary>
    public int CurrencyId { get; init; }

    public string? Memo { get; init; }

    /// <summary>
    /// Idempotency key, shape <c>source:type:id:purpose</c>. Required (non-null)
    /// for every non-<see cref="JournalSource.Manual"/> source. A duplicate
    /// <c>(BookId, IdempotencyKey)</c> returns the existing entry (no throw) — §5.2.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Accrual flag — the period-close step (Phase 3) reverses these into the
    /// next period.
    /// </summary>
    public bool AutoReverseNextPeriod { get; init; }

    /// <summary>
    /// When set, a posting into a <see cref="FiscalPeriodStatus.SoftClosed"/>
    /// period is permitted (audited). HardClosed is always rejected. No override
    /// path is needed in Phase 0 beyond this flag (§5.2).
    /// </summary>
    public bool AllowSoftClosedOverride { get; init; }

    /// <summary>
    /// Optional second-approver principal (maker-checker, §5.7): recorded as the entry's <c>ApprovedBy</c>.
    /// Required (and must differ from the poster) when the entry total exceeds the book's maker-checker
    /// threshold; enforced at the manual-JE edge.
    /// </summary>
    public int? ApprovedByUserId { get; init; }

    public IReadOnlyList<PostingLine> Lines { get; init; } = [];
}

/// <summary>
/// A single debit/credit line of a <see cref="PostingRequest"/>. Exactly one of
/// <see cref="Debit"/>/<see cref="Credit"/> is non-zero. The account is resolved
/// either by an explicit <see cref="GlAccountId"/> or by an
/// <see cref="AccountKey"/> determination lookup — exactly one must be supplied.
/// </summary>
public sealed class PostingLine
{
    /// <summary>
    /// Determination key (e.g. <c>SALES_REVENUE</c>) resolved via
    /// <c>(BookId, Key)</c>. Mutually exclusive with <see cref="GlAccountId"/>.
    /// </summary>
    public string? AccountKey { get; init; }

    /// <summary>Explicit account id (manual JEs). Mutually exclusive with <see cref="AccountKey"/>.</summary>
    public int? GlAccountId { get; init; }

    public int? JobId { get; init; }
    public int? CostCenterId { get; init; }

    /// <summary>Required on lines that resolve to a party-based (AR/AP) control account (§5.2); inventory
    /// control accounts post party-less (reconciled by part via the valuation store, §8.1).</summary>
    public SubledgerPartyType? PartyType { get; init; }
    public int? PartyId { get; init; }

    public decimal Debit { get; init; }
    public decimal Credit { get; init; }

    public string? Description { get; init; }
}
