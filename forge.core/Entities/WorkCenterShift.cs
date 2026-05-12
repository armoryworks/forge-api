using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class WorkCenterShift : BaseEntity
{
    public int WorkCenterId { get; set; }
    public int ShiftId { get; set; }
    public DaysOfWeek DaysOfWeek { get; set; }

    public WorkCenter WorkCenter { get; set; } = null!;
    public Shift Shift { get; set; } = null!;
}
