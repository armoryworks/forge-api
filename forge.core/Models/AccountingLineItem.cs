namespace Forge.Core.Models;

public record AccountingLineItem(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? ItemExternalId);
