
namespace Forge.Core.Entities;

public class ProductionRun : BaseAuditableEntity
{
    public int JobId { get; set; }
    public int PartId { get; set; }
    public int? OperatorId { get; set; }
    public int? WorkCenterId { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public int TargetQuantity { get; set; }
    public int CompletedQuantity { get; set; }
    public int ScrapQuantity { get; set; }
    public int ReworkQuantity { get; set; }
    public ProductionRunStatus Status { get; set; } = ProductionRunStatus.Planned;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public decimal? SetupTimeMinutes { get; set; }
    public decimal? RunTimeMinutes { get; set; }
    public decimal? IdealCycleTimeSeconds { get; set; }
    public decimal? ActualCycleTimeSeconds { get; set; }

    public Job Job { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public WorkCenter? WorkCenter { get; set; }
}
