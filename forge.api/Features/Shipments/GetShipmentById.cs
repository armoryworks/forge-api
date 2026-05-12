using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public record GetShipmentByIdQuery(int Id) : IRequest<ShipmentDetailResponseModel>;

public class GetShipmentByIdHandler(IShipmentRepository repo)
    : IRequestHandler<GetShipmentByIdQuery, ShipmentDetailResponseModel>
{
    public async Task<ShipmentDetailResponseModel> Handle(GetShipmentByIdQuery request, CancellationToken cancellationToken)
    {
        var shipment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        return new ShipmentDetailResponseModel(
            shipment.Id,
            shipment.ShipmentNumber,
            shipment.SalesOrderId,
            shipment.SalesOrder.OrderNumber,
            shipment.SalesOrder.Customer.Name,
            shipment.ShippingAddressId,
            shipment.Status.ToString(),
            shipment.Carrier,
            shipment.TrackingNumber,
            shipment.ShippedDate,
            shipment.DeliveredDate,
            shipment.ShippingCost,
            shipment.Weight,
            shipment.Notes,
            shipment.Invoice?.Id,
            shipment.Lines.Select(l => new ShipmentLineResponseModel(
                l.Id,
                l.SalesOrderLineId,
                l.PartId,
                l.SalesOrderLine?.Description ?? l.Part?.Description ?? string.Empty,
                l.Quantity,
                l.Notes)).ToList(),
            shipment.CreatedAt,
            shipment.UpdatedAt);
    }
}
