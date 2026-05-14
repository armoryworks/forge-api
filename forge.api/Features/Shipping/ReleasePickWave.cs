using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record ReleasePickWaveCommand(int WaveId) : IRequest;

public class ReleasePickWaveHandler(AppDbContext db, IClock clock) : IRequestHandler<ReleasePickWaveCommand>
{
    public async Task Handle(ReleasePickWaveCommand request, CancellationToken cancellationToken)
    {
        var wave = await db.PickWaves
            .FirstOrDefaultAsync(w => w.Id == request.WaveId, cancellationToken)
            ?? throw new KeyNotFoundException($"Pick wave {request.WaveId} not found");

        if (wave.Status != PickWaveStatus.Draft)
            throw new InvalidOperationException($"Cannot release wave in status {wave.Status}");

        wave.Status = PickWaveStatus.Released;
        wave.ReleasedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
