using QBEngineer.Core.Enums;

namespace QBEngineer.Core.Models;

public record CreateLeadRequestModel(
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Source,
    string? Notes,
    DateTimeOffset? FollowUpDate,
    // Wave 7 — engagement-shape axis from the New Lead fork dialog. Defaults
    // to Unknown so the "Quick add" path (skip the axis) lands a valid
    // payload without forcing a value at creation. CustomFieldValues stays
    // free-form JSONB; the fork's per-shape specialised fields land there.
    LeadEngagementShape EngagementShape = LeadEngagementShape.Unknown,
    string? CustomFieldValues = null);
