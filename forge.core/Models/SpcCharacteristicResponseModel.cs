namespace Forge.Core.Models;

public record SpcCharacteristicResponseModel
{
    public int Id { get; init; }
    public int PartId { get; init; }
    public string? PartNumber { get; init; }
    public int? OperationId { get; init; }
    public string? OperationName { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string MeasurementType { get; init; } = string.Empty;
    public decimal NominalValue { get; init; }
    public decimal UpperSpecLimit { get; init; }
    public decimal LowerSpecLimit { get; init; }
    public string? UnitOfMeasure { get; init; }
    public int DecimalPlaces { get; init; }
    public int SampleSize { get; init; }
    public string? SampleFrequency { get; init; }
    public int? GageId { get; init; }
    public bool IsActive { get; init; }
    public bool NotifyOnOoc { get; init; }
    public int MeasurementCount { get; init; }
    public decimal? LatestCpk { get; init; }
}
