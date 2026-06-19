using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipments;

public record SchedulePickupCommand(
    int ShipmentId, DateTimeOffset? ReadyTime, DateTimeOffset? CloseTime, string? Instructions)
    : IRequest<PickupConfirmation>;

public class SchedulePickupValidator : AbstractValidator<SchedulePickupCommand>
{
    public SchedulePickupValidator()
    {
        RuleFor(x => x.ShipmentId).GreaterThan(0);
        When(x => x.ReadyTime.HasValue && x.CloseTime.HasValue, () =>
            RuleFor(x => x.CloseTime!.Value).GreaterThan(x => x.ReadyTime!.Value)
                .WithMessage("Pickup close time must be after the ready time."));
    }
}

public class SchedulePickupHandler(
    IShipmentRepository shipmentRepo,
    IShippingService shippingService,
    AppDbContext db)
    : IRequestHandler<SchedulePickupCommand, PickupConfirmation>
{
    public async Task<PickupConfirmation> Handle(SchedulePickupCommand request, CancellationToken cancellationToken)
    {
        var shipment = await shipmentRepo.FindWithDetailsAsync(request.ShipmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.ShipmentId} not found");

        if (shipment.Status == ShipmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot schedule a pickup for a cancelled shipment.");
        if (!string.IsNullOrEmpty(shipment.PickupConfirmationNumber))
            throw new InvalidOperationException(
                $"A pickup is already scheduled for this shipment (confirmation {shipment.PickupConfirmationNumber}). Cancel it first.");

        // Same carrier resolution as label creation: an assigned, live (Api) carrier with an integration id.
        var carrier = shipment.CarrierId is int cid
            ? await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken)
            : null;
        if (carrier is null)
            throw new InvalidOperationException(
                "No carrier is assigned to this shipment — assign one before scheduling a pickup.");
        if (carrier.IntegrationKind != CarrierIntegrationKind.Api || string.IsNullOrWhiteSpace(carrier.IntegrationServiceId))
            throw new InvalidOperationException(
                $"Carrier {carrier.Name} has no live integration — arrange the pickup manually.");

        var origin = await db.CompanyLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, cancellationToken)
            ?? throw new InvalidOperationException(
                "No default company location is configured — set one before scheduling a pickup.");

        var ready = request.ReadyTime ?? DateTimeOffset.UtcNow;
        var close = request.CloseTime ?? ready.AddHours(8);
        var packageCount = shipment.Packages.Count > 0 ? shipment.Packages.Count : 1;
        var weight = shipment.Packages.Sum(p => p.Weight ?? 0m);
        if (weight <= 0) weight = shipment.Weight ?? 1m;

        var pickupRequest = new PickupRequest(
            new ShippingAddress(origin.Name, origin.Line1, origin.City, origin.State, origin.PostalCode, origin.Country),
            ready, close, packageCount, weight, request.Instructions);

        var confirmation = await shippingService.SchedulePickupAsync(
            pickupRequest, carrier.IntegrationServiceId!, cancellationToken);

        shipment.PickupConfirmationNumber = confirmation.ConfirmationNumber;
        shipment.PickupScheduledDate = confirmation.ScheduledDate;

        db.LogActivityAt("pickup-scheduled",
            $"Pickup scheduled with {carrier.Name} — confirmation {confirmation.ConfirmationNumber}",
            ("Shipment", shipment.Id));

        await shipmentRepo.SaveChangesAsync(cancellationToken);
        return confirmation;
    }
}
