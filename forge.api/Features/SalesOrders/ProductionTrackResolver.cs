using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public static class ProductionTrackResolver
{
    private const string OrderConfirmedStageCode = "order_confirmed";

    public static async Task<(TrackType Track, JobStage StartStage)?> ResolveAsync(AppDbContext db, CancellationToken ct)
    {
        var track = await db.TrackTypes.AsNoTracking()
            .Where(t => t.IsActive && db.JobStages.Any(s => s.TrackTypeId == t.Id && s.IsActive))
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (track is null)
            return null;

        var stages = await db.JobStages.AsNoTracking()
            .Where(s => s.TrackTypeId == track.Id && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var startStage = stages.FirstOrDefault(s => s.Code == OrderConfirmedStageCode) ?? stages[0];
        return (track, startStage);
    }
}
