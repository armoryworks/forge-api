using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>One credit memo line. <c>Taxable</c> null takes the credited invoice line's taxability, else taxable.</summary>
public record CreditMemoLineRequestModel(
    CreditMemoLineType LineType,
    int? PartId,
    int? InvoiceLineId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    bool? Taxable = null);
