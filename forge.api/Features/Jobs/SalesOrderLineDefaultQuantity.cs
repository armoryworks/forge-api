using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public static class SalesOrderLineDefaultQuantity
{
    public static decimal Remaining(decimal ordered, decimal shipped, decimal onJobsInProgress, decimal onCompletedJobs) =>
        Math.Max(0m, ordered - onJobsInProgress - Math.Max(shipped, onCompletedJobs));

    public static async Task<decimal?> ComputeAsync(
        AppDbContext db, int salesOrderLineId, int partId, int? excludeJobId, CancellationToken ct)
    {
        var line = await db.SalesOrderLines
            .Where(l => l.Id == salesOrderLineId)
            .Select(l => new
            {
                l.PartId,
                l.UomId,
                PartStockUomId = l.Part != null ? l.Part.StockUomId : null,
                l.Quantity,
                l.ShippedQuantity,
            })
            .FirstAsync(ct);

        if (line.PartId != partId
            || (line.UomId.HasValue && line.PartStockUomId.HasValue && line.UomId != line.PartStockUomId))
            return null;

        var onOpenJobs = await db.Jobs
            .Where(j => j.SalesOrderLineId == salesOrderLineId && !j.IsArchived && j.Disposition == null
                && j.Id != excludeJobId)
            .SelectMany(j => j.JobParts
                .Where(jp => jp.PartId == j.PartId)
                .Select(jp => new { jp.Quantity, Completed = j.CompletedDate != null }))
            .ToListAsync(ct);

        return Math.Max(1m, Remaining(
            line.Quantity,
            line.ShippedQuantity,
            onOpenJobs.Where(q => !q.Completed).Sum(q => q.Quantity),
            onOpenJobs.Where(q => q.Completed).Sum(q => q.Quantity)));
    }
}
