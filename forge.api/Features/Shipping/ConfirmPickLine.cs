using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record ConfirmPickLineCommand(int WaveId, int LineId, ConfirmPickLineRequestModel Request) : IRequest;

public class ConfirmPickLineHandler(AppDbContext db, IClock clock) : IRequestHandler<ConfirmPickLineCommand>
{
    public async Task Handle(ConfirmPickLineCommand command, CancellationToken cancellationToken)
    {
        var line = await db.PickLines
            .Include(l => l.Wave)
            .FirstOrDefaultAsync(l => l.Id == command.LineId && l.WaveId == command.WaveId, cancellationToken)
            ?? throw new KeyNotFoundException($"Pick line {command.LineId} not found in wave {command.WaveId}");

        if (line.Status != PickLineStatus.Pending)
            throw new InvalidOperationException($"Pick line is already in status {line.Status}");

        line.PickedQuantity = command.Request.PickedQuantity;
        line.PickedAt = clock.UtcNow;
        line.ShortNotes = command.Request.ShortNotes;
        line.Status = command.Request.PickedQuantity >= line.RequestedQuantity
            ? PickLineStatus.Picked
            : PickLineStatus.Short;

        // Update wave progress
        var wave = line.Wave;
        if (wave.Status == PickWaveStatus.Released)
        {
            wave.Status = PickWaveStatus.InProgress;
            wave.StartedAt = clock.UtcNow;
        }

        wave.PickedLines = await db.PickLines
            .CountAsync(l => l.WaveId == wave.Id && (l.Status == PickLineStatus.Picked || l.Status == PickLineStatus.Short), cancellationToken) + 1;

        await db.SaveChangesAsync(cancellationToken);
    }
}
