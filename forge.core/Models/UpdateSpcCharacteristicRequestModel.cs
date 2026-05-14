
namespace Forge.Core.Models;

public record UpdateSpcCharacteristicRequestModel
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public SpcMeasurementType MeasurementType { get; init; }
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
}
