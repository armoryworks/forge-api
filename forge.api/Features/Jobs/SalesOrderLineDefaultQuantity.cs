using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public static class SalesOrderLineDefaultQuantity
{
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
            .SelectMany(j => j.JobParts.Where(jp => jp.PartId == j.PartId))
            .SumAsync(jp => (decimal?)jp.Quantity, ct) ?? 0m;

        return Math.Max(1m, line.Quantity - line.ShippedQuantity - onOpenJobs);
    }
}
