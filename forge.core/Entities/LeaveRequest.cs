using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class LeaveRequest : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int PolicyId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Hours { get; set; }
    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.Pending;
    public int? ApprovedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Reason { get; set; }
    public string? DenialReason { get; set; }

    public LeavePolicy Policy { get; set; } = null!;
}
