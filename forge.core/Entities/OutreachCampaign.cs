
namespace Forge.Core.Entities;

/// <summary>
/// Phase 1r / Batch 5 — wraps a bulk-marketing batch as a first-class
/// entity so cohort reporting can answer "September LinkedIn vs August
/// LinkedIn" without parsing the legacy free-text Source field.
///
/// Carries the strategy, channel, default cooldown override, owner,
/// and date window. Every Lead created from a bulk import gets a
/// nullable <c>CampaignId</c> FK pointing here.
/// </summary>
public class OutreachCampaign : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public BulkLeadIntakeStrategy Strategy { get; set; }

    /// <summary>Optional override for default per-LostReason cooldown — days from now.</summary>
    public int? DefaultCooldownDays { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Soft "is this campaign still actively pulling rows?" flag, distinct from soft-delete.</summary>
    public bool IsActive { get; set; } = true;

    public int? OwnerUserId { get; set; }
}
