
namespace Forge.Core.Entities;

public class Lead : BaseAuditableEntity
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Source { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public string? Notes { get; set; }
    public DateTimeOffset? FollowUpDate { get; set; }
    public string? LostReason { get; set; }
    public int? ConvertedCustomerId { get; set; }
    public string? CustomFieldValues { get; set; }
    public int CreatedBy { get; set; }

    /// <summary>
    /// Wave 7 — classification axis informing sales approach. Defaults to
    /// <see cref="LeadEngagementShape.Unknown"/> so the "Quick add" path
    /// (skip-the-axis fork) round-trips cleanly. See enum doc for the
    /// per-shape sales-motion + intake-form differences.
    /// </summary>
    public LeadEngagementShape EngagementShape { get; set; } = LeadEngagementShape.Unknown;

    /// <summary>
    /// Phase 1r / Batch 5 — optional FK to the bulk-marketing campaign
    /// that produced this lead. Single-entry leads (manual + new-lead
    /// fork) leave it null.
    /// </summary>
    public int? CampaignId { get; set; }

    /// <summary>
    /// Phase 1r / Batch 5 — orthogonal to <see cref="Status"/>. Tracks
    /// where the lead is in the *outreach attempt* lifecycle (queued /
    /// no-answer / voicemail-left / etc.) without overloading the
    /// funnel enum.
    /// </summary>
    public OutreachState OutreachState { get; set; } = OutreachState.Queued;

    /// <summary>Phase 1r / Batch 9 — formal source FK. Legacy free-text Source field stays for back-compat.</summary>
    public int? LeadSourceId { get; set; }

    /// <summary>Phase 1r / Batch 10 — cached ICP score 0-100. Job recomputes on Lead create + rubric edits.</summary>
    public int? IcpScore { get; set; }

    /// <summary>Phase 1r / Batch 11 — rep ownership assignment. Null = unassigned / open queue.</summary>
    public int? AssignedToUserId { get; set; }

    /// <summary>Phase 1r / Batch 12 — optional FK to a multi-contact Account. Null = legacy flat-lead shape.</summary>
    public int? AccountId { get; set; }

    /// <summary>Phase 1r / Batch 13 — "can we actually make this?" gate, distinct from sales lost-reasons.</summary>
    public CapabilityFitStatus CapabilityFit { get; set; } = CapabilityFitStatus.NotAssessed;

    /// <summary>Phase 1r / Batch 14 — NDA lifecycle state. Gates the technical-detail UI sections.</summary>
    public NdaState NdaState { get; set; } = NdaState.None;
    public DateTimeOffset? NdaSignedAt { get; set; }
    public DateTimeOffset? NdaExpiresAt { get; set; }

    /// <summary>Phase 1r / Batch 14 — ITAR/EAR clearance for regulated-tech engagements.</summary>
    public ExportControlClearance ExportControl { get; set; } = ExportControlClearance.NotApplicable;

    /// <summary>Phase 1r / Batch 16 — engineer brought in for technical questions; primary owner stays AssignedToUserId.</summary>
    public int? SecondaryOwnerUserId { get; set; }

    /// <summary>Phase 1r / Batch 16 — RFQ part class for win/loss-by-commodity reports (e.g. "machining-stainless", "injection-plastic").</summary>
    public string? PartClassCode { get; set; }

    public Customer? ConvertedCustomer { get; set; }
    public OutreachCampaign? Campaign { get; set; }
    public LeadSource? LeadSource { get; set; }
    public Account? Account { get; set; }
}
