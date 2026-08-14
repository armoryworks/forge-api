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

    // Navigation
    public Customer Customer { get; set; } = null!;
    public Job? OriginalJob { get; set; }
    public Job? ReworkJob { get; set; }
}
