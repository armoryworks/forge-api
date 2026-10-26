namespace Forge.Core.Models;

public record CreditMemoLineModel(
    int Id,
    int LineNumber,
    string LineType,
    int? PartId,
    string? PartNumber,
    int? InvoiceLineId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    bool Taxable,
    decimal LineTotal);
