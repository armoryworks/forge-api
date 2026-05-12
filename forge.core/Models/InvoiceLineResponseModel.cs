namespace Forge.Core.Models;

public record InvoiceLineResponseModel(
    int Id,
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int LineNumber);
