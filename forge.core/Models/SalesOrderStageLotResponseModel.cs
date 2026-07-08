namespace Forge.Core.Models;

/// <summary>
/// A lot allocated to a sales-order stage (via <c>LotRecord.SalesOrderStageId</c>).
/// </summary>
public record SalesOrderStageLotResponseModel(
    int Id,
    string LotNumber,
    decimal Quantity);
