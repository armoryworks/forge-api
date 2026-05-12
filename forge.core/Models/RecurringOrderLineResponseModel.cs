namespace Forge.Core.Models;

public record RecurringOrderLineResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    int LineNumber);
