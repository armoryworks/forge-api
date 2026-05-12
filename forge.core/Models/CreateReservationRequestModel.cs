namespace Forge.Core.Models;

public record CreateReservationRequestModel(
    int PartId,
    int BinContentId,
    int? JobId,
    int? SalesOrderLineId,
    decimal Quantity,
    string? Notes);
