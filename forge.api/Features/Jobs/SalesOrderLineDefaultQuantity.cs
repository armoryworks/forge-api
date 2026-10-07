using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public static class SalesOrderLineDefaultQuantity
{
    public static decimal Remaining(decimal ordered, decimal shipped, decimal onJobsInProgress, decimal onCompletedJobs) =>
        Math.Max(0m, ordered - onJobsInProgress - Math.Max(shipped, onCompletedJobs));

    public static async Task<decimal> ComputeAsync(
        AppDbContext db, int salesOrderLineId, int? excludeJobId, CancellationToken ct)
    {
        var line = await db.SalesOrderLines
            .Where(l => l.Id == salesOrderLineId)
            .Select(l => new { l.Quantity, l.ShippedQuantity })
            .FirstAsync(ct);

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
