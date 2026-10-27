using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Assets;

public record EndDowntimeLogCommand(int Id) : IRequest<DowntimeLogResponseModel>;

public class EndDowntimeLogHandler(AppDbContext db, IClock clock) : IRequestHandler<EndDowntimeLogCommand, DowntimeLogResponseModel>
{
    public async Task<DowntimeLogResponseModel> Handle(EndDowntimeLogCommand request, CancellationToken ct)
    {
        var log = await db.DowntimeLogs
            .Include(d => d.Asset)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Downtime log {request.Id} not found.");

        if (log.EndedAt.HasValue)
            throw new InvalidOperationException("Downtime event has already ended.");

        log.EndedAt = clock.UtcNow;
        db.LogActivityAt("downtime-ended", $"Downtime ended: {log.Reason}", ("Asset", log.AssetId));
        await db.SaveChangesAsync(ct);

        return new DowntimeLogResponseModel(
            log.Id, log.AssetId, log.Asset.Name, log.ReportedById,
            log.StartedAt, log.EndedAt, log.Reason, log.Resolution,
            log.IsPlanned, log.Notes, log.DurationHours, log.CreatedAt,
            log.WorkCenterId, log.Category);
    }
}
