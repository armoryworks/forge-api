namespace Forge.Core.Models;

public record QuoteLineResponseModel(
    int Id,
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int LineNumber,
    string? Notes);
