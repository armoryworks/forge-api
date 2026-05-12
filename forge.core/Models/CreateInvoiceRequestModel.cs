namespace Forge.Core.Models;

public record CreateInvoiceRequestModel(
    int CustomerId,
    int? SalesOrderId,
    int? ShipmentId,
    DateTimeOffset InvoiceDate,
    DateTimeOffset DueDate,
    string? CreditTerms,
    decimal TaxRate,
    string? Notes,
    List<CreateInvoiceLineModel> Lines);

public record CreateInvoiceLineModel(
    int? PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice);
