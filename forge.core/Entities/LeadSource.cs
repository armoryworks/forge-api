namespace Forge.Core.Entities;

/// <summary>
/// Phase 1r / Batch 9 — formalizes lead-source attribution. The legacy
/// free-text <see cref="Lead.Source"/> stays for backward compat;
/// going forward, bulk-intake stamps a FK to this row instead, and
/// per-source quality scores are computed from disposition outcomes
/// on the linked leads.
///
/// Quality score is rolling — every "BadData" / "Suppressed" /
/// "Engaged" / "Converted" disposition on a child lead nudges the
/// score. A high score means the source's leads convert; a low one
/// means the list provider is delivering garbage.
/// </summary>
public class LeadSource : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Stable identifier admins use to bind this source to imports / forms / referrers.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>0-100. Rolling computation from disposition outcomes on child leads.</summary>
    public int QualityScore { get; set; } = 50;

    /// <summary>Last time the score was recalculated. Job runs nightly.</summary>
    public DateTimeOffset? LastScoredAt { get; set; }

    public bool IsActive { get; set; } = true;
}
