using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ApprovalStep : BaseEntity
{
    public int WorkflowId { get; set; }
    public int StepNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public ApproverType ApproverType { get; set; }
    public int? ApproverUserId { get; set; }
    public string? ApproverRole { get; set; }
    public bool UseDirectManager { get; set; }
    public decimal? AutoApproveBelow { get; set; }
    public int? EscalationHours { get; set; }
    public bool RequireComments { get; set; }
    public bool AllowDelegation { get; set; } = true;

    public ApprovalWorkflow Workflow { get; set; } = null!;
    // No ApplicationUser nav property — FK-only pattern (user is in data project)
}
