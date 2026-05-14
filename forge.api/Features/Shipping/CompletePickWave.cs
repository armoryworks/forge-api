using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record CompletePickWaveCommand(int WaveId) : IRequest;

public class CompletePickWaveHandler(AppDbContext db, IClock clock) : IRequestHandler<CompletePickWaveCommand>
{
    public async Task Handle(CompletePickWaveCommand request, CancellationToken cancellationToken)
    {
        var wave = await db.PickWaves
            .Include(w => w.Lines)
            .FirstOrDefaultAsync(w => w.Id == request.WaveId, cancellationToken)
            ?? throw new KeyNotFoundException($"Pick wave {request.WaveId} not found");

        if (wave.Status != PickWaveStatus.InProgress && wave.Status != PickWaveStatus.Released)
            throw new InvalidOperationException($"Cannot complete wave in status {wave.Status}");

        // Mark remaining pending lines as skipped
        foreach (var line in wave.Lines.Where(l => l.Status == PickLineStatus.Pending))
        {
            line.Status = PickLineStatus.Skipped;
        }

        wave.Status = PickWaveStatus.Completed;
        wave.CompletedAt = clock.UtcNow;
        wave.PickedLines = wave.Lines.Count(l => l.Status == PickLineStatus.Picked || l.Status == PickLineStatus.Short);

        await db.SaveChangesAsync(cancellationToken);
    }
}
