
namespace Forge.Core.Models;

public record LeadResponseModel(
    int Id,
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Source,
    LeadStatus Status,
    string? Notes,
    DateTimeOffset? FollowUpDate,
    string? LostReason,
    int? ConvertedCustomerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Wave 7 — engagement-shape classification axis. Optional default for
    // wire-compat with pre-Wave-7 callers / fixtures.
    LeadEngagementShape EngagementShape = LeadEngagementShape.Unknown,
    string? CustomFieldValues = null,
    // Phase 1j — lifecycle / engagement signals computed from activity.
    /// <summary>Timestamp of the most recent activity-log row for this
    /// lead. Drives the "stale lead" badge — null = nothing's happened
    /// since creation, fall back to CreatedAt.</summary>
    DateTimeOffset? LastActivityAt = null,
    /// <summary>Count of communication-flavoured activity rows in the
    /// last 30 days. Cheap engagement signal — high counts surface as
    /// a chip on the detail surface.</summary>
    int RecentEngagementCount = 0,
    /// <summary>True when the lead is in an active status (not Lost /
    /// Converted) AND has had no activity in the past 14 days. Server-
    /// computed so the threshold stays consistent across UI surfaces.</summary>
    bool IsStale = false,
    /// <summary>Phase 1r / Batch 5 — optional FK to the bulk-marketing
    /// campaign that produced this lead. Null on single-entry leads.</summary>
    int? CampaignId = null,
    /// <summary>Phase 1r / Batch 5 — orthogonal-to-Status outreach
    /// substate (Queued / NoAnswer / VoicemailLeft / Engaged / etc.).</summary>
    OutreachState OutreachState = OutreachState.Queued,
    /// <summary>Phase 1r / Batch 9 — formal source FK.</summary>
    int? LeadSourceId = null,
    /// <summary>Phase 1r / Batch 10 — cached ICP score 0-100, null until computed.</summary>
    int? IcpScore = null,
    /// <summary>Phase 1r / Batch 11 — rep ownership; null = unassigned.</summary>
    int? AssignedToUserId = null,
    /// <summary>Phase 1r / Batch 12 — optional account FK for multi-contact groupings.</summary>
    int? AccountId = null,
    /// <summary>Phase 1r / Batch 13 — capability-fit gate.</summary>
    CapabilityFitStatus CapabilityFit = CapabilityFitStatus.NotAssessed,
    /// <summary>Phase 1r / Batch 14 — NDA lifecycle state.</summary>
    NdaState NdaState = NdaState.None,
    DateTimeOffset? NdaSignedAt = null,
    DateTimeOffset? NdaExpiresAt = null,
    /// <summary>Phase 1r / Batch 14 — ITAR/EAR clearance state.</summary>
    ExportControlClearance ExportControl = ExportControlClearance.NotApplicable,
    /// <summary>Phase 1r / Batch 16 — engineer handoff secondary owner.</summary>
    int? SecondaryOwnerUserId = null,
    /// <summary>Phase 1r / Batch 16 — RFQ part-class code for win/loss-by-commodity reports.</summary>
    string? PartClassCode = null);
