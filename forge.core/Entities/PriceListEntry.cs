namespace Forge.Core.Entities;

public class PriceListEntry : BaseAuditableEntity
{
    public int PriceListId { get; set; }
    public int PartId { get; set; }
    public decimal UnitPrice { get; set; }
    public int MinQuantity { get; set; } = 1;

    /// <summary>
    /// ISO-4217 currency code (e.g. "USD", "EUR"). Defaults to "USD". Per-record
    /// because customer-scoped price lists may legitimately quote in the
    /// customer's preferred currency rather than the install's base currency.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional rationale for the price (mirrors <see cref="PartPrice.Notes"/>).</summary>
    public string? Notes { get; set; }

    public PriceList PriceList { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
