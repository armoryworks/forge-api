namespace Forge.Core.Entities;

public class ApprovalWorkflow : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string? ActivationConditionsJson { get; set; }

    public ICollection<ApprovalStep> Steps { get; set; } = [];
    public ICollection<ApprovalRequest> Requests { get; set; } = [];
}
