
namespace Forge.Core.Entities;

/// <summary>
/// Central barcode registry. Every scannable entity gets a dedicated FK to this table.
/// The Value is auto-generated on entity creation and globally unique across all types.
/// </summary>
public class Barcode : BaseAuditableEntity
{
    public string Value { get; set; } = string.Empty;
    public BarcodeEntityType EntityType { get; set; }
    public bool IsActive { get; set; } = true;

    // ─── Dedicated FKs (exactly one is non-null) ───
    public int? UserId { get; set; }

    public int? PartId { get; set; }
    public Part? Part { get; set; }

    public int? JobId { get; set; }
    public Job? Job { get; set; }

    public int? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    public int? StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }
}
