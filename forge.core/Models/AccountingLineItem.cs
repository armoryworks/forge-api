namespace Forge.Core.Models;

public record AccountingLineItem(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? ItemExternalId,
    bool IsFreight = false,
    bool? Taxable = null);
