using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipping;

public record CreatePickWaveCommand(CreatePickWaveRequestModel Request) : IRequest<PickWaveResponseModel>;

public class CreatePickWaveHandler(AppDbContext db) : IRequestHandler<CreatePickWaveCommand, PickWaveResponseModel>
{
    public async Task<PickWaveResponseModel> Handle(CreatePickWaveCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var alreadyWaved = await db.PickLines.AsNoTracking()
            .Where(pl => request.ShipmentLineIds.Contains(pl.ShipmentLineId) && pl.Wave.Status != PickWaveStatus.Cancelled)
            .Select(pl => pl.ShipmentLineId)
            .ToListAsync(cancellationToken);

        var shipmentLines = await db.ShipmentLines
            .AsNoTracking()
            .Where(sl => request.ShipmentLineIds.Contains(sl.Id) && !alreadyWaved.Contains(sl.Id))
            .Where(sl => sl.Shipment.Status == ShipmentStatus.Pending || sl.Shipment.Status == ShipmentStatus.Packed)
            .ToListAsync(cancellationToken);

        var lines = await PickLinePlanner.PlanAsync(db, shipmentLines, request.Strategy, cancellationToken);
        if (lines.Count == 0)
            throw new InvalidOperationException("None of the chosen shipment lines can be picked: they are already in a wave, shipped, or have no part.");

        var wave = new PickWave
        {
            WaveNumber = await PickWaveNumbers.NextAsync(db, cancellationToken),
            Status = PickWaveStatus.Draft,
            Strategy = request.Strategy,
            AssignedToId = request.AssignedToId,
            TotalLines = lines.Count,
            PickedLines = 0,
            Notes = request.Notes,
            Lines = lines,
        };

        db.PickWaves.Add(wave);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Pick wave {wave.WaveNumber} created with {lines.Count} lines", ("PickWave", wave.Id));
        await db.SaveChangesAsync(cancellationToken);

        return PickWaveResponses.Summary(wave);
    }
}
