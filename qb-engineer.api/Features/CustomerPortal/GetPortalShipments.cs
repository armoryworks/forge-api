using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.CustomerPortal;

public record GetPortalShipmentsQuery(int CustomerId) : IRequest<List<PortalShipmentListItem>>;

public class GetPortalShipmentsHandler(AppDbContext db)
    : IRequestHandler<GetPortalShipmentsQuery, List<PortalShipmentListItem>>
{
    public async Task<List<PortalShipmentListItem>> Handle(GetPortalShipmentsQuery request, CancellationToken ct)
    {
        var shipments = await db.Shipments.AsNoTracking()
            .Include(s => s.SalesOrder)
            .Where(s => s.SalesOrder.CustomerId == request.CustomerId)
            .OrderByDescending(s => s.ShippedDate ?? s.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return shipments.Select(s => new PortalShipmentListItem(
            Id: s.Id,
            ShipmentNumber: s.ShipmentNumber,
            Status: s.Status.ToString(),
            ShippedDate: s.ShippedDate,
            DeliveredDate: s.DeliveredDate,
            Carrier: s.Carrier,
            TrackingNumber: s.TrackingNumber)).ToList();
    }
}
