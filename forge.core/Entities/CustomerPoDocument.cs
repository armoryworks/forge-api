using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// Thin identity record for the auto-generated internal "customer PO"
/// document. Deliberately NOT a PurchaseOrder (that entity is vendor-facing
/// procurement). Lines/totals/status render LIVE from the linked SalesOrder —
/// the document is a view, not a snapshot, so it always reflects downstream
/// updates to the master SO.
/// </summary>
public class CustomerPoDocument : BaseAuditableEntity
{
    public int SalesOrderId { get; set; }
    [MaxLength(30)]
    public string DocumentNumber { get; set; } = string.Empty;
    public int? GeneratedFromQuoteId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public Quote? GeneratedFromQuote { get; set; }
}
