using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public static class SurplusStock
{
    public static async Task<StorageLocation?> ConfiguredLocationAsync(
        AppDbContext db, ISettingsService? settings, CancellationToken ct)
    {
        if (settings is null)
            return null;
        var raw = await settings.GetStringAsync(InventorySettings.SurplusLocationKey, ct);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? await FindActiveBinAsync(db, id, ct)
            : null;
    }

    public static async Task<SurplusSplit> PlanAsync(
        AppDbContext db, ISettingsService? settings, int jobId, int partId, decimal quantity,
        int? requestedSurplusLocationId, int? excludeRunId, CancellationToken ct)
    {
        var none = new SurplusSplit(quantity, 0m, null);
        var configured = await ConfiguredLocationAsync(db, settings, ct);
        if (configured is null || quantity <= 0m)
            return none;

        var remaining = await RemainingDemandAsync(db, jobId, partId, excludeRunId, ct);
        if (remaining is not decimal open)
            return none;

        var demand = Math.Min(quantity, open);
        var surplus = quantity - demand;
        if (surplus <= 0m)
            return none;

        var location = requestedSurplusLocationId is int requested
            ? await FindActiveBinAsync(db, requested, ct) ?? configured
            : configured;
        return new SurplusSplit(demand, surplus, location);
    }

    public static async Task<decimal?> RemainingDemandAsync(
        AppDbContext db, int jobId, int partId, int? excludeRunId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking()
            .Include(j => j.JobParts)
            .Include(j => j.SalesOrderLine)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job?.SalesOrderLineId is null || job.PartId != partId)
            return null;

        var received = await db.ProductionRuns.AsNoTracking()
            .Where(r => r.JobId == jobId && r.PartId == partId && r.ReceivedToStockAt != null
                && (excludeRunId == null || r.Id != excludeRunId))
            .SumAsync(r => (decimal?)r.ReceivedQuantity, ct) ?? 0m;
        return Math.Max(0m, OperationTimeMath.JobTargetQuantity(job) - received);
    }

    private static Task<StorageLocation?> FindActiveBinAsync(AppDbContext db, int locationId, CancellationToken ct) =>
        db.StorageLocations.FirstOrDefaultAsync(l => l.Id == locationId
            && l.DeletedAt == null
            && l.IsActive
            && l.LocationType == LocationType.Bin, ct);
}
