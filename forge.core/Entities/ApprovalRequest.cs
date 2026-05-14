
namespace Forge.Core.Entities;

public class ApprovalRequest : BaseEntity
{
    public int WorkflowId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public int CurrentStepNumber { get; set; }
    public ApprovalRequestStatus Status { get; set; } = ApprovalRequestStatus.Pending;
    public int RequestedById { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public decimal? Amount { get; set; }
    public string? EntitySummary { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }

    public ApprovalWorkflow Workflow { get; set; } = null!;
    // No ApplicationUser nav property — FK-only pattern
    public ICollection<ApprovalDecision> Decisions { get; set; } = [];
}
