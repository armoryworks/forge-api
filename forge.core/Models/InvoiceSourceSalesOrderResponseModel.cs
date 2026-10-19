namespace Forge.Core.Models;

public record InvoiceSourceSalesOrderResponseModel(
    int Id,
    string OrderNumber,
    string CustomerName,
    decimal TaxRate = 0m);
