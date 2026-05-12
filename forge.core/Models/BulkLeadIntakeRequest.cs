using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Wire shape for both /leads/bulk-intake/preview and
/// /leads/bulk-intake/commit. The preview endpoint runs the full
/// dedup + quality pipeline and returns per-row status without
/// persisting; the commit endpoint runs the same pipeline AND
/// inserts the rows that passed.
///
/// Each row carries a client-side <see cref="ExternalRowKey"/> so
/// the frontend can match preview results back to its source rows
/// (the API doesn't otherwise care about the value — it's an
/// opaque round-trip token).
/// </summary>
public record BulkLeadIntakeRequest(
    BulkLeadIntakeStrategy Strategy,
    string? CampaignTag,
    int? CampaignId,
    List<BulkLeadIntakeRow> Rows);

public record BulkLeadIntakeRow(
    string ExternalRowKey,
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Source,
    string? Notes);

public record BulkLeadIntakeResponseModel(
    int TotalRows,
    int CreatedCount,
    int SkippedCount,
    List<BulkLeadIntakeRowResult> Results);

public record BulkLeadIntakeRowResult(
    string ExternalRowKey,
    BulkLeadIntakeRowStatus Status,
    /// <summary>Populated on Created — the new lead id. Null otherwise.</summary>
    int? CreatedLeadId,
    /// <summary>For dedup-skipped rows, the matching existing lead/contact id; for suppressed rows, the prefs row id; null for clean rows.</summary>
    int? MatchedEntityId,
    string? MatchedEntityType,
    /// <summary>Human-readable explanation rendered as a row-level chip in the preview UI.</summary>
    string? Reason);

public enum BulkLeadIntakeRowStatus
{
    /// <summary>Row passed all gates and was created (commit) or would be created (preview).</summary>
    Created,
    /// <summary>Required field for the chosen strategy was missing or empty.</summary>
    MissingRequiredField,
    /// <summary>Email or phone matches an existing Lead row (any status).</summary>
    DuplicateExistingLead,
    /// <summary>Email or phone matches an existing Contact (already a customer).</summary>
    DuplicateExistingContact,
    /// <summary>Within-batch duplicate — earlier row in the same upload had a matching identifier.</summary>
    DuplicateWithinBatch,
    /// <summary>Suppressed by a Lead or Contact opt-out for the strategy's channel.</summary>
    SuppressedOptOut,
    /// <summary>Cooldown window is active on a matching Lead or Contact preferences row.</summary>
    InCooldown,
    /// <summary>Caller-supplied row payload was malformed (e.g. blank companyName) and unrecoverable.</summary>
    Invalid,
}
