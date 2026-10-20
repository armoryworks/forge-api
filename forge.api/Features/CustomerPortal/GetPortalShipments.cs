using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

public record GetPortalShipmentsQuery(int CustomerId) : IRequest<PortalList<PortalShipmentListItem>>;

public class GetPortalShipmentsHandler(AppDbContext db)
    : IRequestHandler<GetPortalShipmentsQuery, PortalList<PortalShipmentListItem>>
{
    public async Task<PortalList<PortalShipmentListItem>> Handle(GetPortalShipmentsQuery request, CancellationToken ct)
    {
        var scope = db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrder.CustomerId == request.CustomerId);
        var shipments = await scope
            .OrderByDescending(s => s.ShippedDate ?? s.CreatedAt)
            .Take(PortalReadRules.ListCap)
            .ToListAsync(ct);
        var totalCount = await PortalReadRules.TotalCountAsync(scope, shipments.Count, ct);

        return new(shipments.Select(s => new PortalShipmentListItem(
            Id: s.Id,
            ShipmentNumber: s.ShipmentNumber,
            Status: s.Status.ToString(),
            ShippedDate: s.ShippedDate,
            DeliveredDate: s.DeliveredDate,
            Carrier: s.Carrier,
            TrackingNumber: s.TrackingNumber)).ToList(), totalCount);
    }
}
