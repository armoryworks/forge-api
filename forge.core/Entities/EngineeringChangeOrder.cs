using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class EngineeringChangeOrder : BaseAuditableEntity
{
    public string EcoNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public EcoChangeType ChangeType { get; set; }
    public EcoStatus Status { get; set; } = EcoStatus.Draft;
    public EcoPriority Priority { get; set; } = EcoPriority.Normal;
    public string? ReasonForChange { get; set; }
    public string? ImpactAnalysis { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public int RequestedById { get; set; }
    public int? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ImplementedAt { get; set; }
    public int? ImplementedById { get; set; }
    public int? ApprovalRequestId { get; set; }

    public ICollection<EcoAffectedItem> AffectedItems { get; set; } = [];
}
