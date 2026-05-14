
namespace Forge.Core.Models;

public record MrpPlannedOrderResponseModel(
    int Id,
    int MrpRunId,
    int PartId,
    string PartNumber,
    string PartDescription,
    MrpOrderType OrderType,
    MrpPlannedOrderStatus Status,
    decimal Quantity,
    DateTimeOffset StartDate,
    DateTimeOffset DueDate,
    bool IsFirmed,
    int? ReleasedPurchaseOrderId,
    int? ReleasedJobId,
    int? ParentPlannedOrderId,
    string? Notes
);
