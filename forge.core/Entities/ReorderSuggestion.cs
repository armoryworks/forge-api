using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ReorderSuggestion : BaseAuditableEntity
{
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;

    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    // Snapshot of stock at analysis time
    public decimal CurrentStock { get; set; }
    public decimal AvailableStock { get; set; }

    // Burn rate data
    public decimal BurnRateDailyAvg { get; set; }
    public int BurnRateWindowDays { get; set; }     // window used (30, 60, or 90)

    // Projection
    public int? DaysOfStockRemaining { get; set; }
    public DateTimeOffset? ProjectedStockoutDate { get; set; }

    // Incoming stock from open POs at analysis time
    public decimal IncomingPoQuantity { get; set; }
    public DateTimeOffset? EarliestPoArrival { get; set; }

    // Suggested order
    public decimal SuggestedQuantity { get; set; }

    // Status lifecycle
    public ReorderSuggestionStatus Status { get; set; } = ReorderSuggestionStatus.Pending;

    // Approval
    public int? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public int? ResultingPurchaseOrderId { get; set; }
    public PurchaseOrder? ResultingPurchaseOrder { get; set; }

    // Dismissal
    public int? DismissedByUserId { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public string? DismissReason { get; set; }

    public string? Notes { get; set; }
}
