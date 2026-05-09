using QBEngineer.Core.Enums;

namespace QBEngineer.Core.Models;

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
    bool IsStale = false);
