
namespace Forge.Core.Entities;

public class AutoPoSuggestion : BaseAuditableEntity
{
    public int PartId { get; set; }
    public int VendorId { get; set; }
    // Phase 3 / WU-23 (F8-broad): auto-PO suggestions on fractional-UoM parts.
    public decimal SuggestedQty { get; set; }
    public DateTimeOffset NeededByDate { get; set; }
    public string? SourceSalesOrderIds { get; set; }
    public AutoPoSuggestionStatus Status { get; set; } = AutoPoSuggestionStatus.Pending;
    public int? ConvertedPurchaseOrderId { get; set; }

    public Part Part { get; set; } = null!;
    public Vendor Vendor { get; set; } = null!;
    public PurchaseOrder? ConvertedPurchaseOrder { get; set; }
}
