
namespace Forge.Core.Entities;

public class PurchaseOrder : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string PONumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public int? JobId { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTimeOffset? SubmittedDate { get; set; }
    public DateTimeOffset? AcknowledgedDate { get; set; }
    public DateTimeOffset? ExpectedDeliveryDate { get; set; }
    public DateTimeOffset? ReceivedDate { get; set; }
    public string? Notes { get; set; }

    // Phase 3 / WU-14 / H3 — short-close audit trail. Set when the PO is
    // closed via /short-close (vendor backorder cancelled, item
    // discontinued); null for normal Closed POs.
    public string? ShortCloseReason { get; set; }
    public DateTimeOffset? ShortClosedAt { get; set; }

    // Blanket PO fields
    public bool IsBlanket { get; set; }
    public decimal? BlanketTotalQuantity { get; set; }
    public decimal? BlanketReleasedQuantity { get; set; }
    public decimal? BlanketRemainingQuantity => BlanketTotalQuantity - BlanketReleasedQuantity;
    public DateTimeOffset? BlanketExpirationDate { get; set; }
    public decimal? AgreedUnitPrice { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    // ─── Bought-parts effort PR2 — landed cost foundation ──────────────
    /// <summary>
    /// Commercial term governing this PO's freight + insurance + duty
    /// allocation. Defaults from the preferred VendorPart's Incoterm at
    /// PO creation; overridable per PO when a vendor offers a one-off
    /// arrangement. See <see cref="Incoterm"/>.
    /// </summary>
    public Incoterm Incoterm { get; set; } = Incoterm.FOB_Origin;

    /// <summary>
    /// Buyer's freight estimate at PO time — drives PO total + cash-flow
    /// forecast. Actual freight is captured per-receipt in
    /// <c>ReceivingRecord.ActualFreight</c> (PR3) and feeds landed cost.
    /// Null = "no freight estimate captured" (distinct from $0.00 freight,
    /// which means "vendor confirmed free shipping").
    /// </summary>
    public decimal? EstimatedFreight { get; set; }

    /// <summary>
    /// Currency the vendor quoted in. Defaults from the preferred
    /// VendorPart's Currency at PO creation. May differ from the tenant's
    /// functional currency for international purchases.
    /// </summary>
    public string QuoteCurrency { get; set; } = "USD";

    /// <summary>
    /// Locked FX rate (quote currency → tenant functional currency)
    /// captured at PO confirmation (Draft → Confirmed transition). Used
    /// by the cost tab + landed cost calc. Pre-confirm POs have this
    /// null — quoted total is informational only until lock.
    /// </summary>
    public decimal? FxRate { get; set; }

    /// <summary>
    /// Free-text source/reference for the FxRate snapshot (e.g.,
    /// "ECB 2026-05-06" or "manual entry"). Helpful for audit when an FX
    /// variance is investigated post-payment.
    /// </summary>
    public string? FxRateSource { get; set; }

    public Vendor Vendor { get; set; } = null!;
    public Job? Job { get; set; }
    public ICollection<PurchaseOrderLine> Lines { get; set; } = [];
    public ICollection<PurchaseOrderRelease> Releases { get; set; } = [];
}
