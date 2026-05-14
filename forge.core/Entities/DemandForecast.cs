
namespace Forge.Core.Entities;

public class DemandForecast : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int PartId { get; set; }
    public ForecastMethod Method { get; set; }
    public ForecastStatus Status { get; set; } = ForecastStatus.Draft;
    public int HistoricalPeriods { get; set; } = 12;
    public int ForecastPeriods { get; set; } = 6;
    public double? SmoothingFactor { get; set; }
    public DateTimeOffset ForecastStartDate { get; set; }
    public string? ForecastDataJson { get; set; }
    public int? AppliedToMasterScheduleId { get; set; }
    public int? CreatedByUserId { get; set; }

    public Part Part { get; set; } = null!;
    public MasterSchedule? AppliedToMasterSchedule { get; set; }
    public ICollection<ForecastOverride> Overrides { get; set; } = [];
}
