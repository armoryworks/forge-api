namespace Forge.Core.Models;

/// <summary>An open sales-order line for the affected part with quantity still to ship.</summary>
public record EcoImpactSalesOrderLineModel(
    int SalesOrderId,
    string OrderNumber,
    int LineId,
    int LineNumber,
    string CustomerName,
    decimal OpenQuantity,
    string Status);
