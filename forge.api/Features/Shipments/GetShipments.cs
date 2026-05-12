using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public record GetShipmentsQuery(int? SalesOrderId, ShipmentStatus? Status) : IRequest<List<ShipmentListItemModel>>;

public class GetShipmentsHandler(IShipmentRepository repo)
    : IRequestHandler<GetShipmentsQuery, List<ShipmentListItemModel>>
{
    public async Task<List<ShipmentListItemModel>> Handle(GetShipmentsQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetAllAsync(request.SalesOrderId, request.Status, cancellationToken);
    }
}
