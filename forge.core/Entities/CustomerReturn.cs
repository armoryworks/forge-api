using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class CustomerReturn : BaseAuditableEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// The job the returned goods came off, when there was one.
    ///
    /// <para>Nullable since the retail lane landed. A made-to-order B2B part is
    /// always traceable to a job, but a retail return is usually a stocked item
    /// picked from a bin — there is no job, and requiring one would have forced
    /// either a fake job per return or a second parallel returns entity. Use
    /// <see cref="SalesOrderLineId"/> to identify what came back when this is
    /// null.</para>
    /// </summary>
    public int? OriginalJobId { get; set; }

    /// <summary>
    /// The order line being returned. The primary link for retail returns, and a
    /// useful cross-check on job-based ones. Together with
    /// <see cref="OriginalJobId"/> this covers both lanes without a second
    /// entity: at least one of the two is always set.
    /// </summary>
    public int? SalesOrderLineId { get; set; }
    public SalesOrderLine? SalesOrderLine { get; set; }

    public int? ReworkJobId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public CustomerReturnStatus Status { get; set; } = CustomerReturnStatus.Received;
    public DateTimeOffset ReturnDate { get; set; }
    public int? InspectedById { get; set; }
    public DateTimeOffset? InspectedAt { get; set; }
    public string? InspectionNotes { get; set; }

    /// <summary>
    /// The channel the return came in through, when it did not originate in
    /// Forge. Marketplace returns are authorised on the platform — the buyer
    /// clicks "return" on Amazon, not in our system — so the RMA arrives
    /// already approved and we are recording it, not deciding it.
    /// </summary>
    public int? ChannelId { get; set; }
    public SalesChannel? Channel { get; set; }

    /// <summary>The platform's own RMA identifier, so support can match a buyer's reference to our record.</summary>
    public string? ExternalRmaId { get; set; }

    /// <summary>
    /// Amount refunded to the buyer. On a marketplace the refund is issued by
    /// the platform and later appears as a negative settlement line; recording
    /// it here is what lets the two be reconciled.
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>Quantity returned. Null on legacy job-based returns, which predate line-level tracking.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>The invoice the returned goods were billed on, when known. Drives the return's credit and original cost.</summary>
    public int? InvoiceId { get; set; }

    /// <summary>The invoice line the returned goods were billed on, when known.</summary>
    public int? InvoiceLineId { get; set; }

    /// <summary>What was done with the goods when the return was resolved. Null until then.</summary>
    public ReturnDisposition? Disposition { get; set; }

    public decimal? QuantityRestocked { get; set; }

    public decimal? QuantityScrapped { get; set; }

    public int? RestockLocationId { get; set; }

    public string? RestockLotNumber { get; set; }

    /// <summary>The bin movement that put returned goods back into stock.</summary>
    public int? RestockBinMovementId { get; set; }

    /// <summary>The credit memo issued for the return.</summary>
    public int? CreditMemoId { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public Job? OriginalJob { get; set; }
    public Job? ReworkJob { get; set; }
    public Invoice? Invoice { get; set; }
    public InvoiceLine? InvoiceLine { get; set; }
    public StorageLocation? RestockLocation { get; set; }
    public CreditMemo? CreditMemo { get; set; }
}
