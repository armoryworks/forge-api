using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record AutoGeneratePickWaveCommand(AutoWaveParametersModel Parameters) : IRequest<PickWaveResponseModel>;

public class AutoGeneratePickWaveHandler(AppDbContext db) : IRequestHandler<AutoGeneratePickWaveCommand, PickWaveResponseModel>
{
    public async Task<PickWaveResponseModel> Handle(AutoGeneratePickWaveCommand command, CancellationToken cancellationToken)
    {
        // Find shipment lines not already assigned to a pick wave
        var existingPickLineShipmentIds = await db.PickLines
            .AsNoTracking()
            .Select(pl => pl.ShipmentLineId)
            .ToListAsync(cancellationToken);

        var pendingShipmentLines = await db.ShipmentLines
            .AsNoTracking()
            .Include(sl => sl.Shipment)
            .Where(sl => !existingPickLineShipmentIds.Contains(sl.Id))
            .Where(sl => sl.Shipment.Status == ShipmentStatus.Pending)
            .Take(command.Parameters.MaxLinesPerWave)
            .ToListAsync(cancellationToken);

        if (pendingShipmentLines.Count == 0)
        {
            throw new InvalidOperationException("No pending shipment lines available for wave generation");
        }

        var lastWave = await db.PickWaves
            .AsNoTracking()
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var nextNumber = (lastWave?.Id ?? 0) + 1;

        var wave = new PickWave
        {
            WaveNumber = $"WAVE-{nextNumber:D4}",
            Status = PickWaveStatus.Draft,
            Strategy = command.Parameters.Strategy,
            AssignedToId = command.Parameters.AssignedToId,
            TotalLines = pendingShipmentLines.Count,
            PickedLines = 0,
        };

        db.PickWaves.Add(wave);
        await db.SaveChangesAsync(cancellationToken);

        var sortOrder = 0;
        foreach (var sl in pendingShipmentLines)
        {
            sortOrder++;
            db.PickLines.Add(new PickLine
            {
                WaveId = wave.Id,
                ShipmentLineId = sl.Id,
                PartId = sl.PartId ?? 0,
                FromLocationId = 1,
                RequestedQuantity = sl.Quantity,
                PickedQuantity = 0,
                Status = PickLineStatus.Pending,
                SortOrder = sortOrder,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new PickWaveResponseModel
        {
            Id = wave.Id,
            WaveNumber = wave.WaveNumber,
            Status = wave.Status,
            Strategy = wave.Strategy,
            AssignedToId = wave.AssignedToId,
            TotalLines = wave.TotalLines,
            PickedLines = 0,
            CreatedAt = wave.CreatedAt,
        };
    }
}
