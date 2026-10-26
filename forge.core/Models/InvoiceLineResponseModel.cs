using Forge.Core.Enums;

namespace Forge.Core.Models;

public record InvoiceLineResponseModel(
    int Id,
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int LineNumber,
    bool Taxable = true,
    DocumentLineType LineType = DocumentLineType.Item);
