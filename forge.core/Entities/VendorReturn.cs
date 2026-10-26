using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A return to vendor (RTV): stock an NCR dispositioned Return To Vendor, held in QcHold until it ships. Shipping
/// relieves the held stock and its value into the vendor returns clearing account; the vendor's credit memo clears it.
/// The return takes the receipt's purchase order currency and rate, so the credit is valued as the goods came in.
/// </summary>
public class VendorReturn : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned.</summary>
    public uint Version { get; set; }

    /// <summary>Human-readable number, <c>RTV-000001</c>.</summary>
    public string ReturnNumber { get; set; } = string.Empty;

    public int VendorId { get; set; }

    public int? NonConformanceId { get; set; }

    public int? PurchaseOrderId { get; set; }

    public int? PurchaseOrderLineId { get; set; }

    public int? ReceivingRecordId { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>The receipt's rate (transaction to functional) the goods were valued at.</summary>
    public decimal FxRate { get; set; } = 1m;

    public VendorReturnStatus Status { get; set; } = VendorReturnStatus.Open;

    public DateTimeOffset? ShipDate { get; set; }

    public string? Carrier { get; set; }

    public string? TrackingNumber { get; set; }

    /// <summary>The vendor's return authorization number.</summary>
    public string? RmaNumber { get; set; }

    public string? Notes { get; set; }

    public int? ShippedByUserId { get; set; }

    public int? CreatedByUserId { get; set; }

    /// <summary>Σ quantity × unit price in the return's currency: what the vendor is expected to credit.</summary>
    public decimal ExpectedCredit => Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));

    public Vendor Vendor { get; set; } = null!;
    public NonConformance? NonConformance { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Currency Currency { get; set; } = null!;
    public ICollection<VendorReturnLine> Lines { get; set; } = [];
}
