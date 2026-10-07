using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GetInvoiceSourcesQuery(int CustomerId) : IRequest<InvoiceSourcesResponseModel>;

public class GetInvoiceSourcesHandler(AppDbContext db)
    : IRequestHandler<GetInvoiceSourcesQuery, InvoiceSourcesResponseModel>
{
    private static readonly ShipmentStatus[] ShippedStatuses =
        [ShipmentStatus.Shipped, ShipmentStatus.InTransit, ShipmentStatus.Delivered];

    public async Task<InvoiceSourcesResponseModel> Handle(GetInvoiceSourcesQuery request, CancellationToken cancellationToken)
    {
        var salesOrders = await db.SalesOrders.AsNoTracking()
            .Where(so => so.CustomerId == request.CustomerId)
            .OrderByDescending(so => so.CreatedAt)
            .Select(so => new InvoiceSourceSalesOrderResponseModel(
                so.Id, so.OrderNumber, so.Customer.Name))
            .ToListAsync(cancellationToken);

        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrder.CustomerId == request.CustomerId
                && ShippedStatuses.Contains(s.Status)
                && !db.Invoices.Any(i => i.ShipmentId == s.Id))
            .OrderByDescending(s => s.ShippedDate ?? s.CreatedAt)
            .Select(s => new InvoiceSourceShipmentResponseModel(
                s.Id, s.ShipmentNumber, s.SalesOrderId, s.SalesOrder.OrderNumber, s.ShippedDate))
            .ToListAsync(cancellationToken);

        return new InvoiceSourcesResponseModel(salesOrders, shipments);
    }
}
