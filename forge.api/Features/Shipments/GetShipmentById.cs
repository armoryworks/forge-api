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
            shipment.CarrierId,
            shipment.TrackingNumber,
            shipment.ScanCode,
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
                // Prefer the SO-line description, then the part's description, then
                // the part's Name (the canonical short identifier post Phase-4) —
                // treating blanks as missing so a part with only a Name still shows.
                FirstNonBlank(l.SalesOrderLine?.Description, l.Part?.Description, l.Part?.Name),
                l.Quantity,
                l.Notes)).ToList(),
            shipment.CreatedAt,
            shipment.UpdatedAt);
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
