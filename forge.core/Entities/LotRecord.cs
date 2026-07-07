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
    // S4c staged scheduling — a lot may be allocated to one SO stage (same
    // multi-link pattern as JobId/ProductionRunId/PurchaseOrderLineId).
    public int? SalesOrderStageId { get; set; }

    public Part Part { get; set; } = null!;
    public Job? Job { get; set; }
    public ProductionRun? ProductionRun { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public SalesOrderStage? SalesOrderStage { get; set; }
}
