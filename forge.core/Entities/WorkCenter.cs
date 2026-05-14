
namespace Forge.Core.Entities;

public class WorkCenter : BaseAuditableEntity
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public int? CompanyLocationId { get; set; }
    public int? AssetId { get; set; }
    public decimal DailyCapacityHours { get; set; } = 8m;
    public decimal EfficiencyPercent { get; set; } = 100m;
    public int NumberOfMachines { get; set; } = 1;
    public decimal LaborCostPerHour { get; set; }
    public decimal BurdenRatePerHour { get; set; }
    public decimal? IdealCycleTimeSeconds { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public CompanyLocation? Location { get; set; }
    public Asset? Asset { get; set; }
    public ICollection<WorkCenterShift> Shifts { get; set; } = [];
    public ICollection<WorkCenterCalendar> CalendarOverrides { get; set; } = [];
    public ICollection<Operation> Operations { get; set; } = [];
}
