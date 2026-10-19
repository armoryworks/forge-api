using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public static class AndonDowntime
{
    public static bool StopsTheWorkCenter(AndonAlertType type) => type == AndonAlertType.Stoppage;

    public static async Task OpenAsync(AppDbContext db, AndonAlert alert, ILogger logger, CancellationToken ct)
    {
        if (!StopsTheWorkCenter(alert.Type))
            return;

        var assetId = await ResolveAssetIdAsync(db, alert.WorkCenterId, ct);
        if (assetId is null)
        {
            logger.LogWarning("Stoppage andon on work center {WorkCenterId} logged no downtime: the work center has no machine asset", alert.WorkCenterId);
            return;
        }

        db.DowntimeLogs.Add(new DowntimeLog
        {
            AssetId = assetId.Value,
            WorkCenterId = alert.WorkCenterId,
            ReportedById = alert.RequestedById,
            StartedAt = alert.RequestedAt,
            IsPlanned = false,
            Reason = "Andon stoppage",
            Description = alert.Notes,
            JobId = alert.JobId,
        });
    }

    public static async Task CloseAsync(AppDbContext db, AndonAlert alert, DateTimeOffset endedAt, CancellationToken ct)
    {
        if (!StopsTheWorkCenter(alert.Type))
            return;

        var openLog = await db.DowntimeLogs
            .Where(d => d.WorkCenterId == alert.WorkCenterId
                && !d.IsPlanned
                && d.EndedAt == null
                && d.StartedAt == alert.RequestedAt)
            .OrderBy(d => d.Id)
            .FirstOrDefaultAsync(ct);

        if (openLog is not null)
            openLog.EndedAt = endedAt;
    }

    private static async Task<int?> ResolveAssetIdAsync(AppDbContext db, int workCenterId, CancellationToken ct)
    {
        var linkedAssetId = await db.WorkCenters
            .AsNoTracking()
            .Where(w => w.Id == workCenterId)
            .Select(w => w.AssetId)
            .FirstOrDefaultAsync(ct);
        if (linkedAssetId is not null)
            return linkedAssetId;

        return await db.Assets
            .AsNoTracking()
            .Where(a => a.WorkCenterId == workCenterId && a.AssetType != AssetType.Tooling)
            .OrderBy(a => a.Id)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(ct);
    }
}
