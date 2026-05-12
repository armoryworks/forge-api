namespace Forge.Core.Entities;

public class Reservation : BaseAuditableEntity
{
    public int PartId { get; set; }
    public int BinContentId { get; set; }
    public int? JobId { get; set; }
    public int? SalesOrderLineId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }

    public Part Part { get; set; } = null!;
    public BinContent BinContent { get; set; } = null!;
    public Job? Job { get; set; }
    public SalesOrderLine? SalesOrderLine { get; set; }
}
