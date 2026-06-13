using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Shipments;

public record ShipShipmentCommand(int Id) : IRequest;

public class ShipShipmentHandler(
    IShipmentRepository repo,
    // Operational inventory relief (BE-1 / INV-SH2): when supplied (production DI), shipping relieves
    // on-hand stock for each line (FIFO bin decrement + a Ship movement, idempotent per line). Null in the
    // mock-based handler tests that don't register it → ship flips status only, as before.
    InventoryReliefService? relief = null,
    IHttpContextAccessor? httpContext = null,
    ILogger<ShipShipmentHandler>? logger = null)
    : IRequestHandler<ShipShipmentCommand>
{
    public async Task Handle(ShipShipmentCommand request, CancellationToken cancellationToken)
    {
        // Load with Lines + each line's SalesOrderLine so relief can resolve PartId; fall back to the lean
        // load when relief isn't wired (keeps the no-relief path identical to before).
        var shipment = (relief is not null
            ? await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            : await repo.FindAsync(request.Id, cancellationToken))
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status != ShipmentStatus.Pending && shipment.Status != ShipmentStatus.Packed)
            throw new InvalidOperationException("Only Pending or Packed shipments can be shipped");

        // Relieve on-hand inventory before flipping status. Backorder-tolerant: a stock shortfall is
        // soft-logged rather than blocking the ship (the relief service's documented caller option) — stock
        // that was never tracked, or backordered lines, must still be shippable. Relief is idempotent per
        // line (InventoryRelievedAt guard), so a later re-ship won't double-decrement.
        if (relief is not null)
        {
            var userId = int.TryParse(
                httpContext?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                out var uid) ? uid : 0;
            try
            {
                await relief.RelieveShipmentAsync(shipment, userId, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogWarning(ex,
                    "Inventory relief shortfall shipping shipment {ShipmentId}; shipping anyway (backorder).",
                    shipment.Id);
            }
        }

        shipment.Status = ShipmentStatus.Shipped;
        shipment.ShippedDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
