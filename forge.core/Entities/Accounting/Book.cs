namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A set of books (a single accounting entity / legal entity). Forge is
/// single-entity now but multi-entity-ready: every GL entity carries
/// <c>BookId</c> from day one and the engine enforces book-consistency (§5.1).
/// <para>
/// Ledger entities derive from <see cref="BaseEntity"/> (NOT
/// <c>BaseAuditableEntity</c>) so they are exempt from the global soft-delete
/// query filter — a soft-deleted journal line silently dropping from a summed
/// trial balance would "balance" yet be wrong (§2, §5.6).
/// </para>
/// </summary>
public class Book : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Functional (base) currency of this book.</summary>
    public int FunctionalCurrencyId { get; set; }

    /// <summary>
    /// IANA time zone (e.g. "America/New_York") that anchors
    /// <c>EntryDate</c>→period resolution. Immune to UTC normalization.
    /// </summary>
    public string ReportingTimeZone { get; set; } = string.Empty;

    /// <summary>
    /// Money rounding tolerance for posting balance checks. Default = the
    /// functional currency's smallest unit (e.g. 0.01).
    /// </summary>
    public decimal RoundingTolerance { get; set; }

    public bool IsActive { get; set; } = true;

    public Currency FunctionalCurrency { get; set; } = null!;
}
