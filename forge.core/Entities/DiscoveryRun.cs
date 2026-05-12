namespace Forge.Core.Entities;

/// <summary>
/// Phase 4 Phase-F — Audit trail row for a single discovery walkthrough.
/// One row written when an admin applies a discovery recommendation. Captures
/// the answer set verbatim, the preset the engine recommended, the preset
/// the admin actually applied (may differ if they picked an alternative),
/// the delta that was applied, and the consultant-mode flag.
///
/// Per 4F Phase-F decision D2: re-running discovery overwrites previous
/// capability state but does NOT rewrite or replace prior DiscoveryRun rows.
/// The history accumulates as immutable evidence of every configuration
/// decision.
/// </summary>
public class DiscoveryRun : BaseAuditableEntity
{
    /// <summary>User who ran discovery (matches AuditLogEntry.UserId).</summary>
    public int RunByUserId { get; set; }

    /// <summary>When the wizard started (the user's first answer submission).</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the apply occurred. Same as CreatedAt in practice.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>JSON-serialised answer set: array of {questionId, value} objects.</summary>
    public string AnswersJson { get; set; } = "[]";

    /// <summary>The preset the engine recommended.</summary>
    public string RecommendedPresetId { get; set; } = string.Empty;

    /// <summary>The preset the admin actually applied (may differ from recommended).</summary>
    public string AppliedPresetId { get; set; } = string.Empty;

    /// <summary>Confidence value the engine produced for the recommended preset (0.0-1.0).</summary>
    public double RecommendedConfidence { get; set; }

    /// <summary>JSON-serialised list of the deltas applied: [{code, currentlyEnabled, willBeEnabled}, ...].</summary>
    public string AppliedDeltasJson { get; set; } = "[]";

    /// <summary>True if the wizard ran in consultant mode (deepdive questions surfaced).</summary>
    public bool RanInConsultantMode { get; set; }
}
