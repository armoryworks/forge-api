using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

/// <summary>
/// Shipping workspace ready-to-ship queue: open sales orders (Confirmed / InProduction /
/// PartiallyShipped) that still have line quantity left to ship, with their unshipped lines.
/// </summary>
public record GetReadyToShipQueueQuery : IRequest<List<ReadyToShipOrderModel>>;

public class GetReadyToShipQueueHandler(AppDbContext db)
    : IRequestHandler<GetReadyToShipQueueQuery, List<ReadyToShipOrderModel>>
{
    private static readonly SalesOrderStatus[] OpenStatuses =
    [
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.InProduction,
        SalesOrderStatus.PartiallyShipped,
    ];

    public async Task<List<ReadyToShipOrderModel>> Handle(GetReadyToShipQueueQuery request, CancellationToken ct)
    {
        // Bounded to open orders that still have something to ship — never a full-table load.
        var orders = await db.SalesOrders
            .AsNoTracking()
            .Include(so => so.Customer)
            .Include(so => so.Lines).ThenInclude(l => l.Part)
            .Where(so => OpenStatuses.Contains(so.Status)
                && so.Lines.Any(l => l.Quantity - l.ShippedQuantity > 0))
            .ToListAsync(ct);

        return orders
            .OrderBy(so => so.RequestedDeliveryDate ?? DateTimeOffset.MaxValue)
            .Select(so => new ReadyToShipOrderModel(
                so.Id,
                so.OrderNumber,
                so.CustomerId,
                so.Customer.GetDisplayName(),
                so.ShippingAddressId,
                so.RequestedDeliveryDate,
                so.Status.ToString(),
                so.Lines
                    .Where(l => l.RemainingQuantity > 0)
                    .OrderBy(l => l.LineNumber)
                    .Select(l => new ReadyToShipLineModel(
                        l.Id,
                        l.LineNumber,
                        l.Description,
                        l.PartId,
                        l.Part?.PartNumber,
                        l.Quantity,
                        l.ShippedQuantity,
                        l.RemainingQuantity))
                    .ToList()))
            .ToList();
    }
}
