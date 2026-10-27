using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipping;

public record AutoGeneratePickWaveCommand(AutoWaveParametersModel Parameters) : IRequest<PickWaveResponseModel>;

public class AutoGeneratePickWaveHandler(AppDbContext db) : IRequestHandler<AutoGeneratePickWaveCommand, PickWaveResponseModel>
{
    public async Task<PickWaveResponseModel> Handle(AutoGeneratePickWaveCommand command, CancellationToken cancellationToken)
    {
        var parameters = command.Parameters;
        var maxLines = Math.Clamp(parameters.MaxLinesPerWave, 1, 500);

        var query = db.ShipmentLines
            .AsNoTracking()
            .Where(sl => sl.PartId != null && sl.Quantity > 0)
            .Where(sl => sl.Shipment.Status == ShipmentStatus.Pending || sl.Shipment.Status == ShipmentStatus.Packed)
            .Where(sl => !db.PickLines.Any(pl => pl.ShipmentLineId == sl.Id && pl.Wave.Status != PickWaveStatus.Cancelled));

        if (parameters.ShipByDateFrom is DateTimeOffset from)
            query = query.Where(sl => sl.Shipment.SalesOrder.RequestedDeliveryDate >= from);
        if (parameters.ShipByDateTo is DateTimeOffset to)
            query = query.Where(sl => sl.Shipment.SalesOrder.RequestedDeliveryDate <= to);

        var ordered = query
            .OrderBy(sl => sl.Shipment.SalesOrder.RequestedDeliveryDate == null)
            .ThenBy(sl => sl.Shipment.SalesOrder.RequestedDeliveryDate)
            .ThenBy(sl => sl.ShipmentId)
            .ThenBy(sl => sl.Id);

        List<ShipmentLine> pendingShipmentLines;
        if (parameters.Strategy == PickWaveStrategy.WaveByCarrier)
        {
            var first = await ordered.Select(sl => new { sl.Shipment.CarrierId, sl.Shipment.Carrier }).FirstOrDefaultAsync(cancellationToken);
            pendingShipmentLines = first is null
                ? []
                : await ordered
                    .Where(sl => sl.Shipment.CarrierId == first.CarrierId && sl.Shipment.Carrier == first.Carrier)
                    .Take(maxLines)
                    .ToListAsync(cancellationToken);
        }
        else
        {
            pendingShipmentLines = await ordered.Take(maxLines).ToListAsync(cancellationToken);
        }

        if (pendingShipmentLines.Count == 0)
            throw new InvalidOperationException("No open shipment lines are waiting to be picked for that window.");

        var lines = await PickLinePlanner.PlanAsync(db, pendingShipmentLines, parameters.Strategy, cancellationToken);

        var wave = new PickWave
        {
            WaveNumber = await PickWaveNumbers.NextAsync(db, cancellationToken),
            Status = PickWaveStatus.Draft,
            Strategy = parameters.Strategy,
            AssignedToId = parameters.AssignedToId,
            TotalLines = lines.Count,
            PickedLines = 0,
            Lines = lines,
        };

        db.PickWaves.Add(wave);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Pick wave {wave.WaveNumber} generated with {lines.Count} lines", ("PickWave", wave.Id));
        await db.SaveChangesAsync(cancellationToken);

        return PickWaveResponses.Summary(wave);
    }
}
