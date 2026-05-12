namespace Forge.Core.Entities;

public class LeavePolicy : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal AccrualRatePerPayPeriod { get; set; }
    public decimal? MaxBalance { get; set; }
    public decimal? CarryOverLimit { get; set; }
    public bool AccrueFromHireDate { get; set; } = true;
    public int? WaitingPeriodDays { get; set; }
    public bool IsPaidLeave { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public ICollection<LeaveBalance> Balances { get; set; } = [];
    public ICollection<LeaveRequest> Requests { get; set; } = [];
}
