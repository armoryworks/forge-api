using Forge.Core.Enums;

namespace Forge.Core.Models;

public record InvoiceSourceSalesOrderResponseModel(
    int Id,
    string OrderNumber,
    string CustomerName,
    decimal TaxRate = 0m,
    CreditTerms? CreditTerms = null,
    int? CurrencyId = null,
    List<InvoiceSourceShipmentLineResponseModel>? FreightLines = null);
