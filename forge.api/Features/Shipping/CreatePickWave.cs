using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record CreatePickWaveCommand(CreatePickWaveRequestModel Request) : IRequest<PickWaveResponseModel>;

public class CreatePickWaveHandler(AppDbContext db) : IRequestHandler<CreatePickWaveCommand, PickWaveResponseModel>
{
    public async Task<PickWaveResponseModel> Handle(CreatePickWaveCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // Generate wave number
        var lastWave = await db.PickWaves
            .AsNoTracking()
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var nextNumber = (lastWave?.Id ?? 0) + 1;
        var waveNumber = $"WAVE-{nextNumber:D4}";

        var wave = new PickWave
        {
            WaveNumber = waveNumber,
            Status = PickWaveStatus.Draft,
            Strategy = request.Strategy,
            AssignedToId = request.AssignedToId,
            TotalLines = request.ShipmentLineIds.Count,
            PickedLines = 0,
            Notes = request.Notes,
        };

        db.PickWaves.Add(wave);
        await db.SaveChangesAsync(cancellationToken);

        var shipmentLines = await db.ShipmentLines
            .AsNoTracking()
            .Where(sl => request.ShipmentLineIds.Contains(sl.Id))
            .ToListAsync(cancellationToken);

        var sortOrder = 0;
        foreach (var sl in shipmentLines)
        {
            sortOrder++;
            db.PickLines.Add(new PickLine
            {
                WaveId = wave.Id,
                ShipmentLineId = sl.Id,
                PartId = sl.PartId ?? 0,
                FromLocationId = 1, // Default location — will be resolved by optimization
                RequestedQuantity = sl.Quantity,
                PickedQuantity = 0,
                Status = PickLineStatus.Pending,
                SortOrder = sortOrder,
            });
        }

        wave.TotalLines = sortOrder;
        await db.SaveChangesAsync(cancellationToken);

        return new PickWaveResponseModel
        {
            Id = wave.Id,
            WaveNumber = wave.WaveNumber,
            Status = wave.Status,
            Strategy = wave.Strategy,
            AssignedToId = wave.AssignedToId,
            TotalLines = wave.TotalLines,
            PickedLines = wave.PickedLines,
            Notes = wave.Notes,
            CreatedAt = wave.CreatedAt,
        };
    }
}
