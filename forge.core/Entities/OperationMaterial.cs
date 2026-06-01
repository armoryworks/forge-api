namespace Forge.Core.Entities;

public class OperationMaterial : BaseAuditableEntity
{
    public int OperationId { get; set; }
    public int BomLineId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? Notes { get; set; }

    public Operation Operation { get; set; } = null!;
    public BOMLine BomLine { get; set; } = null!;
}
