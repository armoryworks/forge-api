using QBEngineer.Core.Enums;

namespace QBEngineer.Core.Entities;

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

    public Customer? ConvertedCustomer { get; set; }
}
