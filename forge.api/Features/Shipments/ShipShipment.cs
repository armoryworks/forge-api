using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record ShipShipmentCommand(int Id, string? ScanCode = null) : IRequest;

public class ShipShipmentHandler(
    IShipmentRepository repo,
    // Operational inventory relief (BE-1 / INV-SH2): when supplied (production DI), shipping relieves
    // on-hand stock for each line (FIFO bin decrement + a Ship movement, idempotent per line). Null in the
    // mock-based handler tests that don't register it → ship flips status only, as before.
    InventoryReliefService? relief = null,
    IHttpContextAccessor? httpContext = null,
    ILogger<ShipShipmentHandler>? logger = null,
    // Optional / null-default so the mock-based handler tests stay constructible; the DI path supplies
    // it. Backs the scan-to-ship gate (carrier lookup) and the shipped activity-log row.
    AppDbContext? db = null)
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

        // Scan-to-ship gate (carrier epic). When the shipment is assigned a set-up carrier that
        // requires it, the worker must scan the shipment's printed Forge label QR — its coverage-bound
        // ScanCode — before it can ship. A swapped or stale label won't match. No assigned carrier
        // (legacy / free-text) or a carrier that opts out → no gate, unchanged flow.
        if (db is not null && shipment.CarrierId is int carrierId)
        {
            var carrier = await db.Carriers
                .Where(c => c.Id == carrierId)
                .Select(c => new { c.RequiresScanToShip, c.IntegrationKind })
                .FirstOrDefaultAsync(cancellationToken);

            // Integrated (API) carrier: shipping is performed by CREATING THE LABEL, which assigns the
            // tracking number and hands the parcel off to the carrier. Marking it shipped by hand without
            // a label would record a shipment the carrier never received — block it until a label exists.
            if (carrier?.IntegrationKind == CarrierIntegrationKind.Api
                && string.IsNullOrWhiteSpace(shipment.TrackingNumber))
                throw new InvalidOperationException(
                    "This shipment uses an integrated carrier — create the shipping label (which assigns the " +
                    "tracking number) before marking it shipped.");

            if ((carrier?.RequiresScanToShip ?? false)
                && (string.IsNullOrWhiteSpace(request.ScanCode)
                    || !string.Equals(request.ScanCode, shipment.ScanCode, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Scan the shipment's printed label to confirm it before marking it shipped.");
        }

        var userId = int.TryParse(
            httpContext?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out var uid) ? uid : 0;

        // Relieve on-hand inventory before flipping status. Backorder-tolerant: a stock shortfall is
        // soft-logged rather than blocking the ship (the relief service's documented caller option) — stock
        // that was never tracked, or backordered lines, must still be shippable. Relief is idempotent per
        // line (InventoryRelievedAt guard), so a later re-ship won't double-decrement.
        if (relief is not null)
        {
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

        // Transactional entity logs on itself (CLAUDE.md Activity-logging rules). Same scoped context as
        // the repo, so the row flushes with the status change below.
        db?.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "Shipment",
            EntityId = shipment.Id,
            UserId = userId == 0 ? null : userId,
            Action = "shipped",
            Description = $"Shipment {shipment.ShipmentNumber} marked shipped.",
        });

        await repo.SaveChangesAsync(cancellationToken);
    }
}
