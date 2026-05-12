using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ApprovalDecision : BaseEntity
{
    public int RequestId { get; set; }
    public int StepNumber { get; set; }
    public int DecidedById { get; set; }
    public ApprovalDecisionType Decision { get; set; }
    public string? Comments { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public int? DelegatedToUserId { get; set; }

    public ApprovalRequest Request { get; set; } = null!;
    // No ApplicationUser nav properties — FK-only pattern
}
