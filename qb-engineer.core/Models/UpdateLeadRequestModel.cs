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
    string? CustomFieldValues = null);
