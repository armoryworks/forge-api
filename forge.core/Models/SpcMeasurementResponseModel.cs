namespace Forge.Core.Models;

public record SpcMeasurementResponseModel
{
    public int Id { get; init; }
    public int CharacteristicId { get; init; }
    public int? JobId { get; init; }
    public int? ProductionRunId { get; init; }
    public string? LotNumber { get; init; }
    public string MeasuredByName { get; init; } = string.Empty;
    public DateTimeOffset MeasuredAt { get; init; }
    public int SubgroupNumber { get; init; }
    public decimal[] Values { get; init; } = [];
    public decimal Mean { get; init; }
    public decimal Range { get; init; }
    public decimal StdDev { get; init; }
    public decimal Median { get; init; }
    public bool IsOutOfSpec { get; init; }
    public bool IsOutOfControl { get; init; }
    public string? OocRuleViolated { get; init; }
    public string? Notes { get; init; }
}
