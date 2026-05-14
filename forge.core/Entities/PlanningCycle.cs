
namespace Forge.Core.Entities;

public class PlanningCycle : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public string? Goals { get; set; }
    public PlanningCycleStatus Status { get; set; } = PlanningCycleStatus.Planning;
    public int DurationDays { get; set; } = 14;

    public ICollection<PlanningCycleEntry> Entries { get; set; } = [];
}
