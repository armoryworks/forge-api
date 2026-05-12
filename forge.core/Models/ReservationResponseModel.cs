namespace Forge.Core.Models;

public record ReservationResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string PartDescription,
    int BinContentId,
    string LocationPath,
    int? JobId,
    string? JobTitle,
    string? JobNumber,
    int? SalesOrderLineId,
    decimal Quantity,
    string? Notes,
    DateTimeOffset CreatedAt);
