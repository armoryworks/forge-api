using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

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

    /// <summary>
    /// Where this PO came from (F11 provenance): manual entry (with the user),
    /// MRP auto-generation, quote-driven automation, or an external system.
    /// </summary>
    public PoOriginSource OriginSource { get; set; } = PoOriginSource.Manual;
    public int? OriginUserId { get; set; }
    [MaxLength(200)]
    public string? OriginReference { get; set; }

    public int? VendorContactId { get; set; }
    public int? VendorAddressId { get; set; }
    public int? ShipToLocationId { get; set; }

    // ─── Bought-parts effort PR2 — landed cost foundation ──────────────
    /// <summary>
    /// Commercial term governing this PO's freight + insurance + duty
    /// allocation. Defaults from the preferred VendorPart's Incoterm at
    /// PO creation; overridable per PO when a vendor offers a one-off
    /// arrangement. See <see cref="Enums.Incoterm"/>.
    /// </summary>
    public Enums.Incoterm Incoterm { get; set; } = Enums.Incoterm.FOB_Origin;

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
    /// Currency the PO is denominated in. Kept in step with <see cref="QuoteCurrency"/> on every write.
    /// Null on rows from before multi-currency = the functional currency.
    /// </summary>
    public int? CurrencyId { get; set; }

    /// <summary>Payment terms the PO's bills start from. Null = the vendor's terms.</summary>
    public int? PaymentTermsId { get; set; }

    /// <summary>Standard, drop-ship (vendor ships to the customer) or back-to-back (bought for one sales order line).</summary>
    public FulfilmentType FulfilmentType { get; set; } = FulfilmentType.Standard;

    /// <summary>The customer address a drop-ship PO tells the vendor to ship to. Null on every other PO.</summary>
    public int? DropShipAddressId { get; set; }

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
    public Currency? Currency { get; set; }
    public Job? Job { get; set; }
    public VendorContact? VendorContact { get; set; }
    public VendorAddress? VendorAddress { get; set; }
    public CompanyLocation? ShipToLocation { get; set; }
    public CustomerAddress? DropShipAddress { get; set; }
    public ICollection<PurchaseOrderLine> Lines { get; set; } = [];
    public ICollection<PurchaseOrderRelease> Releases { get; set; } = [];
}
