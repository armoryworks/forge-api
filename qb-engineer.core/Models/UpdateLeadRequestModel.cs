using QBEngineer.Core.Enums;

namespace QBEngineer.Core.Models;

public record UpdateLeadRequestModel(
    string? CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Source,
    LeadStatus? Status,
    string? Notes,
    DateTimeOffset? FollowUpDate,
    string? LostReason,
    // Wave 7 — reclassify a lead's engagement shape after creation. Null =
    // leave unchanged. Setting to Unknown explicitly clears the prior
    // classification (matches the rest of the patch fields' nullable +
    // explicit-clear semantics).
    LeadEngagementShape? EngagementShape = null,
    string? CustomFieldValues = null,
    // Phase 1r / Batch 13-14 — manufacturing/compliance classifications.
    // Each is independently nullable so the detail panel's per-chip edit
    // affordances PATCH only the field they're modifying.
    CapabilityFitStatus? CapabilityFit = null,
    NdaState? NdaState = null,
    DateTimeOffset? NdaSignedAt = null,
    DateTimeOffset? NdaExpiresAt = null,
    ExportControlClearance? ExportControl = null,
    // Phase 1r / Batch 12 — multi-contact B2B parent grouping. Setting null
    // explicitly clears the link (lead becomes flat / unaffiliated).
    int? AccountId = null);
