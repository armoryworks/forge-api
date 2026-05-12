using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record EndDowntimeLogCommand(int Id) : IRequest<DowntimeLogResponseModel>;

public class EndDowntimeLogHandler(AppDbContext db) : IRequestHandler<EndDowntimeLogCommand, DowntimeLogResponseModel>
{
    public async Task<DowntimeLogResponseModel> Handle(EndDowntimeLogCommand request, CancellationToken ct)
    {
        var log = await db.DowntimeLogs
            .Include(d => d.Asset)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Downtime log {request.Id} not found.");

        if (log.EndedAt.HasValue)
            throw new InvalidOperationException("Downtime event has already ended.");

        log.EndedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new DowntimeLogResponseModel(
            log.Id, log.AssetId, log.Asset.Name, log.ReportedById,
            log.StartedAt, log.EndedAt, log.Reason, log.Resolution,
            log.IsPlanned, log.Notes, log.DurationHours, log.CreatedAt);
    }
}
