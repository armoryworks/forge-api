namespace Forge.Core.Entities;

public class SpcMeasurement : BaseEntity
{
    public int CharacteristicId { get; set; }
    public int? JobId { get; set; }
    public int? ProductionRunId { get; set; }
    public string? LotNumber { get; set; }
    public int MeasuredById { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
    public int SubgroupNumber { get; set; }
    public string ValuesJson { get; set; } = "[]";
    public decimal Mean { get; set; }
    public decimal Range { get; set; }
    public decimal StdDev { get; set; }
    public decimal Median { get; set; }
    public bool IsOutOfSpec { get; set; }
    public bool IsOutOfControl { get; set; }
    public string? OocRuleViolated { get; set; }
    public string? Notes { get; set; }

    public SpcCharacteristic Characteristic { get; set; } = null!;
    public Job? Job { get; set; }
}
