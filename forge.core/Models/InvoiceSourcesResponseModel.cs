namespace Forge.Core.Models;

public record InvoiceSourcesResponseModel(
    List<InvoiceSourceSalesOrderResponseModel> SalesOrders,
    List<InvoiceSourceShipmentResponseModel> Shipments);
