using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ Phase-3 — a reusable / recurring journal-entry template (standard allocations, rent, recurring
/// accruals). Posting from a template builds a normal balanced <c>PostingRequest</c> for a given entry date
/// and runs it through the engine; the template itself is not a ledger row.
/// </summary>
public class JournalTemplate : BaseAuditableEntity
{
    public int BookId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Journal source stamped on entries posted from this template (default Manual).</summary>
    public JournalSource Source { get; set; } = JournalSource.Manual;

    /// <summary>Default memo for posted entries (the post call may override).</summary>
    public string? Memo { get; set; }

    /// <summary>When true, the posted entry auto-reverses at the next period close (recurring accruals).</summary>
    public bool AutoReverseNextPeriod { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<JournalTemplateLine> Lines { get; set; } = [];
}

/// <summary>One line of a <see cref="JournalTemplate"/> (mirrors a posting line — key OR explicit account).</summary>
public class JournalTemplateLine : BaseEntity
{
    public int JournalTemplateId { get; set; }
    public int LineNumber { get; set; }

    /// <summary>Determination key (resolved at post time). Mutually exclusive with <see cref="GlAccountId"/>.</summary>
    public string? AccountDeterminationKey { get; set; }

    /// <summary>Explicit account. Mutually exclusive with <see cref="AccountDeterminationKey"/>.</summary>
    public int? GlAccountId { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }

    public SubledgerPartyType? PartyType { get; set; }
    public int? PartyId { get; set; }

    public JournalTemplate JournalTemplate { get; set; } = null!;
}
