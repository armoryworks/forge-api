namespace Forge.Core.Models;

public record CreateBackToBackRequestModel
{
    public int VendorId { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitCost { get; init; }
    public DateTimeOffset? ExpectedDeliveryDate { get; init; }
    public string? Notes { get; init; }
}
