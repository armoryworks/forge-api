using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MasterSchedule : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MasterScheduleStatus Status { get; set; } = MasterScheduleStatus.Draft;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public int CreatedByUserId { get; set; }

    public ICollection<MasterScheduleLine> Lines { get; set; } = [];
}
