namespace Forge.Core.Entities;

public class UomConversion : BaseEntity
{
    public int FromUomId { get; set; }
    public int ToUomId { get; set; }
    public decimal ConversionFactor { get; set; }
    public int? PartId { get; set; }
    public bool IsReversible { get; set; } = true;

    public UnitOfMeasure FromUom { get; set; } = null!;
    public UnitOfMeasure ToUom { get; set; } = null!;
    public Part? Part { get; set; }
}
