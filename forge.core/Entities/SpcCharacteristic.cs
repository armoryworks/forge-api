using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class SpcCharacteristic : BaseAuditableEntity
{
    public int PartId { get; set; }
    public int? OperationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SpcMeasurementType MeasurementType { get; set; } = SpcMeasurementType.Variable;
    public decimal NominalValue { get; set; }
    public decimal UpperSpecLimit { get; set; }
    public decimal LowerSpecLimit { get; set; }
    public string? UnitOfMeasure { get; set; }
    public int DecimalPlaces { get; set; } = 4;
    public int SampleSize { get; set; } = 5;
    public string? SampleFrequency { get; set; }
    public int? GageId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NotifyOnOoc { get; set; } = true;

    public Part Part { get; set; } = null!;
    public Operation? Operation { get; set; }
    public ICollection<SpcMeasurement> Measurements { get; set; } = [];
    public ICollection<SpcControlLimit> ControlLimits { get; set; } = [];
}
