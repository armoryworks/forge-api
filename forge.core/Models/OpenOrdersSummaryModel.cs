namespace Forge.Core.Models;

public record OpenOrdersSummaryModel(
    int TotalOrders,
    int ConfirmedCount,
    int InProductionCount,
    int PartiallyShippedCount,
    decimal TotalValue);
