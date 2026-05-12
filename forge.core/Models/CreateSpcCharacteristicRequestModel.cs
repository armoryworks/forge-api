using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateSpcCharacteristicRequestModel
{
    public int PartId { get; init; }
    public int? OperationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public SpcMeasurementType MeasurementType { get; init; } = SpcMeasurementType.Variable;
    public decimal NominalValue { get; init; }
    public decimal UpperSpecLimit { get; init; }
    public decimal LowerSpecLimit { get; init; }
    public string? UnitOfMeasure { get; init; }
    public int DecimalPlaces { get; init; } = 4;
    public int SampleSize { get; init; } = 5;
    public string? SampleFrequency { get; init; }
    public int? GageId { get; init; }
    public bool NotifyOnOoc { get; init; } = true;
}
