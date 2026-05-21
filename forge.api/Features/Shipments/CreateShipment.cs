using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public record CreateShipmentCommand(
    int SalesOrderId,
    int? ShippingAddressId,
    string? Carrier,
    string? TrackingNumber,
    decimal? ShippingCost,
    decimal? Weight,
    string? Notes,
    List<CreateShipmentLineModel> Lines) : IRequest<ShipmentListItemModel>;

public class CreateShipmentValidator : AbstractValidator<CreateShipmentCommand>
{
    public CreateShipmentValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l).Must(l =>
                    (l.SalesOrderLineId.HasValue && l.SalesOrderLineId > 0) ||
                    (l.PartId.HasValue && l.PartId > 0))
                .WithMessage("Each line must reference either a Sales Order Line or a Part");
            // Phase 3 / WU-23 (F8-broad): decimal quantity supports fractional UoM.
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
        });
    }
}

public class CreateShipmentHandler(IShipmentRepository shipmentRepo, ISalesOrderRepository orderRepo, IMediator mediator, IHttpContextAccessor httpContext)
    : IRequestHandler<CreateShipmentCommand, ShipmentListItemModel>
{
    public async Task<ShipmentListItemModel> Handle(CreateShipmentCommand request, CancellationToken cancellationToken)
    {
        var order = await orderRepo.FindWithDetailsAsync(request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        if (order.Status == SalesOrderStatus.Draft || order.Status == SalesOrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot create shipment for Draft or Cancelled orders");

        var shipmentNumber = await shipmentRepo.GenerateNextShipmentNumberAsync(cancellationToken);

        var shipment = new Shipment
        {
            ShipmentNumber = shipmentNumber,
            SalesOrderId = request.SalesOrderId,
            ShippingAddressId = request.ShippingAddressId ?? order.ShippingAddressId,
            Carrier = request.Carrier,
            TrackingNumber = request.TrackingNumber,
            ShippingCost = request.ShippingCost,
            Weight = request.Weight,
            Notes = request.Notes,
        };

        foreach (var line in request.Lines)
        {
            if (line.SalesOrderLineId.HasValue)
            {
                // SO-line based: validate remaining quantity and update fulfillment
                var orderLine = order.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId)
                    ?? throw new KeyNotFoundException($"Sales order line {line.SalesOrderLineId} not found");

                if (line.Quantity > orderLine.RemainingQuantity)
                    throw new InvalidOperationException(
                        $"Cannot ship {line.Quantity} of line {orderLine.LineNumber} — only {orderLine.RemainingQuantity} remaining");

                orderLine.ShippedQuantity += line.Quantity;

                shipment.Lines.Add(new ShipmentLine
                {
                    SalesOrderLineId = line.SalesOrderLineId,
                    PartId = orderLine.PartId,
                    Quantity = line.Quantity,
                    Notes = line.Notes,
                });
            }
            else
            {
                // Part-based: create line without SO line fulfillment tracking
                shipment.Lines.Add(new ShipmentLine
                {
                    PartId = line.PartId,
                    Quantity = line.Quantity,
                    Notes = line.Notes,
                });
            }
        }

        // Update order status based on fulfillment (only applies when SO lines are linked)
        if (order.Lines.Any() && order.Lines.All(l => l.IsFullyShipped))
            order.Status = SalesOrderStatus.Shipped;
        else if (order.Lines.Any(l => l.ShippedQuantity > 0))
            order.Status = SalesOrderStatus.PartiallyShipped;

        await shipmentRepo.AddAsync(shipment, cancellationToken);
        await shipmentRepo.SaveChangesAsync(cancellationToken);

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await mediator.Publish(new ShipmentCreatedEvent(shipment.Id, request.SalesOrderId, userId), cancellationToken);

        return new ShipmentListItemModel(
            shipment.Id, shipment.ShipmentNumber, shipment.SalesOrderId,
            order.OrderNumber, order.Customer.Name, shipment.Status.ToString(),
            shipment.Carrier, shipment.TrackingNumber, shipment.ShippedDate,
            shipment.CreatedAt);
    }
}
