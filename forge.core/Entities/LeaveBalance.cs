namespace Forge.Core.Entities;

public class LeaveBalance : BaseEntity
{
    public int UserId { get; set; }
    public int PolicyId { get; set; }
    public decimal Balance { get; set; }
    public decimal UsedThisYear { get; set; }
    public decimal AccruedThisYear { get; set; }
    public DateTimeOffset LastAccrualDate { get; set; }

    public LeavePolicy Policy { get; set; } = null!;
}
