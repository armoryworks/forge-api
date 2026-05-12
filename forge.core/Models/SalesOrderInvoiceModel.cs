namespace Forge.Core.Models;

public record SalesOrderInvoiceModel(
    int Id,
    string InvoiceNumber,
    string Status,
    decimal TotalAmount,
    DateTimeOffset? DueDate,
    string PaymentStatus,
    List<string> ShipmentNumbers);
