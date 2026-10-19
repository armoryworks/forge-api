using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public sealed record JobEstimate(decimal Material, decimal Labor, decimal Burden, decimal Subcontract)
{
    public static readonly JobEstimate Zero = new(0m, 0m, 0m, 0m);
}

public static class JobEstimates
{
    public static async Task<JobEstimate> ComputeAsync(
        AppDbContext db, IStandardCostResolver standardCost, int? partId, decimal quantity, CancellationToken ct)
    {
        if (partId is not int id || quantity <= 0m)
            return JobEstimate.Zero;

        var unit = await standardCost.ResolveAsync(id, ct);
        var subcontractUnit = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == id && o.IsSubcontract)
            .SumAsync(o => o.SubcontractCost ?? 0m, ct);

        return new JobEstimate(
            decimal.Round(unit.Material * quantity, 2),
            decimal.Round(unit.Labor * quantity, 2),
            decimal.Round(unit.Overhead * quantity, 2),
            decimal.Round(subcontractUnit * quantity, 2));
    }

    public static async Task SnapshotAsync(
        AppDbContext db, IStandardCostResolver? standardCost, Job job, decimal quantity, CancellationToken ct)
    {
        if (standardCost is null)
            return;

        var estimate = await ComputeAsync(db, standardCost, job.PartId, quantity, ct);
        job.EstimatedMaterialCost = estimate.Material;
        job.EstimatedLaborCost = estimate.Labor;
        job.EstimatedBurdenCost = estimate.Burden;
        job.EstimatedSubcontractCost = estimate.Subcontract;
    }

    public static bool IsUnset(decimal material, decimal labor, decimal burden, decimal subcontract) =>
        material == 0m && labor == 0m && burden == 0m && subcontract == 0m;
}
