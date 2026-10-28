using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetReservableSalesOrderLinesQuery(int PartId, int? SalesOrderId) : IRequest<List<ReservableSalesOrderLineResponseModel>>;

public class GetReservableSalesOrderLinesHandler(AppDbContext db)
    : IRequestHandler<GetReservableSalesOrderLinesQuery, List<ReservableSalesOrderLineResponseModel>>
{
    public async Task<List<ReservableSalesOrderLineResponseModel>> Handle(GetReservableSalesOrderLinesQuery request, CancellationToken cancellationToken)
    {
        var query = SalesOrderLineReservations.Reservable(db, request.PartId).AsNoTracking();
        if (request.SalesOrderId is int salesOrderId)
            query = query.Where(l => l.SalesOrderId == salesOrderId);

        var lines = await query
            .OrderBy(l => l.SalesOrder.RequestedDeliveryDate == null)
            .ThenBy(l => l.SalesOrder.RequestedDeliveryDate)
            .ThenBy(l => l.SalesOrder.OrderNumber)
            .ThenBy(l => l.LineNumber)
            .Select(l => new
            {
                l.Id,
                l.SalesOrderId,
                l.SalesOrder.OrderNumber,
                l.LineNumber,
                CustomerName = l.SalesOrder.Customer.CompanyName ?? l.SalesOrder.Customer.Name,
                Open = l.Quantity - l.ShippedQuantity - l.ShortClosedQuantity,
                l.SalesOrder.RequestedDeliveryDate,
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        var reserved = await SalesOrderLineReservations.ReservedByLineAsync(db, lines.Select(l => l.Id).ToList(), cancellationToken);

        return lines.Select(l => new ReservableSalesOrderLineResponseModel(
            l.Id, l.SalesOrderId, l.OrderNumber, l.LineNumber,
            string.IsNullOrWhiteSpace(l.CustomerName) ? string.Empty : l.CustomerName,
            l.Open, reserved.GetValueOrDefault(l.Id), l.RequestedDeliveryDate)).ToList();
    }
}
