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
    string? CustomFieldValues = null);
