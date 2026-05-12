namespace Forge.Core.Entities;

public class LotRecord : BaseAuditableEntity
{
    public string LotNumber { get; set; } = string.Empty;
    public int PartId { get; set; }
    public int? JobId { get; set; }
    public int? ProductionRunId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    // Phase 3 / WU-23 (F8-broad): UoM-aware fractional quantities for material-
    // by-weight / volume / length lots.
    public decimal Quantity { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? SupplierLotNumber { get; set; }
    public string? Notes { get; set; }

    public Part Part { get; set; } = null!;
    public Job? Job { get; set; }
    public ProductionRun? ProductionRun { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
}
