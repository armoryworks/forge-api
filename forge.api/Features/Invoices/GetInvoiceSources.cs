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
    public const int MaxResults = 200;

    private static readonly ShipmentStatus[] ShippedStatuses =
        [ShipmentStatus.Shipped, ShipmentStatus.InTransit, ShipmentStatus.Delivered];

    private static readonly SalesOrderStatus[] InvoiceableOrderStatuses =
        [SalesOrderStatus.Confirmed, SalesOrderStatus.InProduction, SalesOrderStatus.PartiallyShipped, SalesOrderStatus.Shipped];

    public async Task<InvoiceSourcesResponseModel> Handle(GetInvoiceSourcesQuery request, CancellationToken cancellationToken)
    {
        var salesOrders = await db.SalesOrders.AsNoTracking()
            .Where(so => so.CustomerId == request.CustomerId && InvoiceableOrderStatuses.Contains(so.Status))
            .OrderByDescending(so => so.CreatedAt)
            .Take(MaxResults)
            .Select(so => new InvoiceSourceSalesOrderResponseModel(
                so.Id, so.OrderNumber, so.Customer.Name))
            .ToListAsync(cancellationToken);

        var candidates = await db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrder.CustomerId == request.CustomerId
                && InvoiceableOrderStatuses.Contains(s.SalesOrder.Status)
                && ShippedStatuses.Contains(s.Status)
                && !db.Invoices.Any(i => i.ShipmentId == s.Id))
            .OrderByDescending(s => s.ShippedDate ?? s.CreatedAt)
            .Select(s => new InvoiceSourceShipmentResponseModel(
                s.Id, s.ShipmentNumber, s.SalesOrderId, s.SalesOrder.OrderNumber, s.ShippedDate))
            .ToListAsync(cancellationToken);

        var fullyInvoiced = await FullyInvoicedSalesOrderIdsAsync(
            candidates.Select(s => s.SalesOrderId).Distinct().ToList(), cancellationToken);

        var shipments = candidates
            .Where(s => !fullyInvoiced.Contains(s.SalesOrderId))
            .Take(MaxResults)
            .ToList();

        return new InvoiceSourcesResponseModel(salesOrders, shipments);
    }

    private async Task<HashSet<int>> FullyInvoicedSalesOrderIdsAsync(List<int> salesOrderIds, CancellationToken ct)
    {
        if (salesOrderIds.Count == 0) return [];

        var shipped = await db.ShipmentLines
            .Where(sl => sl.PartId != null
                && salesOrderIds.Contains(sl.Shipment.SalesOrderId)
                && ShippedStatuses.Contains(sl.Shipment.Status))
            .GroupBy(sl => new { sl.Shipment.SalesOrderId, PartId = sl.PartId!.Value })
            .Select(g => new { g.Key.SalesOrderId, g.Key.PartId, Qty = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var invoiced = await db.InvoiceLines
            .Where(il => il.PartId != null
                && il.Invoice.SalesOrderId != null
                && salesOrderIds.Contains(il.Invoice.SalesOrderId.Value))
            .GroupBy(il => new { SalesOrderId = il.Invoice.SalesOrderId!.Value, PartId = il.PartId!.Value })
            .Select(g => new { g.Key.SalesOrderId, g.Key.PartId, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => (x.SalesOrderId, x.PartId), x => x.Qty, ct);

        return shipped
            .GroupBy(s => s.SalesOrderId)
            .Where(g => g.All(s => invoiced.GetValueOrDefault((s.SalesOrderId, s.PartId)) >= s.Qty))
            .Select(g => g.Key)
            .ToHashSet();
    }
}
