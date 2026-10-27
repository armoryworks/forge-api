using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class LeaveBalanceAdjustment : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int PolicyId { get; set; }
    public LeaveAdjustmentType AdjustmentType { get; set; }
    public decimal Hours { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string? Reason { get; set; }
    public decimal BalanceAfter { get; set; }
    public int? CreatedByUserId { get; set; }

    public LeavePolicy Policy { get; set; } = null!;
}
