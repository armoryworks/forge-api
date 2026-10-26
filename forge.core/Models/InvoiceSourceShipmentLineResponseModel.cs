using Forge.Core.Enums;

namespace Forge.Core.Models;

public record InvoiceSourceShipmentLineResponseModel(
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    bool Taxable = true,
    int? SalesOrderLineId = null,
    DocumentLineType LineType = DocumentLineType.Item);
