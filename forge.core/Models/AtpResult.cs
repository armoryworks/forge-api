namespace Forge.Core.Models;

public record AtpResult
{
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public decimal RequestedQuantity { get; init; }
    public decimal OnHand { get; init; }
    public decimal AllocatedToOrders { get; init; }
    public decimal ScheduledReceipts { get; init; }
    public decimal AvailableToPromise { get; init; }
    public DateOnly? EarliestAvailableDate { get; init; }
    public bool CanFulfill { get; init; }
}
