using Forge.Core.Enums.Accounting;

namespace Forge.Core.Entities.Accounting;

/// <summary>
/// §7A conversion — an opening stock load: quantity and cutover unit cost per part, location and lot, posted as
/// bin contents plus valuation-store receipts and (in <see cref="StockLoadGlMode.OffsetEquity"/> mode) one
/// journal Dr inventory / Cr Opening Balance Equity, so on-hand stock, the valuation and the ledger agree from
/// day one.
/// </summary>
public class InventoryStockLoad : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Human-readable number, <c>SL-000001</c>.</summary>
    public string LoadNumber { get; set; } = string.Empty;

    public int BookId { get; set; }

    /// <summary>The cutover date; the journal's entry date.</summary>
    public DateOnly AsOfDate { get; set; }

    public StockLoadStatus Status { get; set; } = StockLoadStatus.Draft;

    public StockLoadGlMode GlMode { get; set; } = StockLoadGlMode.OffsetEquity;

    /// <summary>Name of the CSV the lines came from, when uploaded.</summary>
    public string? SourceFileName { get; set; }

    public string? Notes { get; set; }

    /// <summary>Σ line extended value (currency-rounded).</summary>
    public decimal TotalValue { get; set; }

    /// <summary>The posted opening-stock journal (OffsetEquity mode only).</summary>
    public long? JournalEntryId { get; set; }

    public int? CreatedByUserId { get; set; }

    public int? PostedByUserId { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }

    public uint Version { get; set; }

    public ICollection<InventoryStockLoadLine> Lines { get; set; } = [];
}
