namespace Forge.Core.Entities;

public class InterPlantTransferLine : BaseEntity
{
    public int TransferId { get; set; }
    public int PartId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? ReceivedQuantity { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public string? LotNumber { get; set; }

    public InterPlantTransfer Transfer { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
