using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public static class KioskWork
{
    private static readonly Expression<Func<Job, bool>> OpenAtShopFloorRule = j => !j.IsArchived
        && j.CompletedDate == null
        && j.Disposition == null
        && j.CurrentStage.IsShopFloor;

    private static readonly Func<Job, bool> OpenAtShopFloorCheck = OpenAtShopFloorRule.Compile();

    public static IQueryable<Job> ReadyToStart(AppDbContext db) =>
        db.Jobs.Where(OpenAtShopFloorRule).Where(j => j.AssigneeId == null);

    public static bool IsOpenAtShopFloor(Job job) => OpenAtShopFloorCheck(job);

    public static async Task<Dictionary<int, KioskNextOperationResponseModel>> NextOperationsAsync(
        AppDbContext db, IEnumerable<(int JobId, int? PartId)> jobs, CancellationToken ct)
    {
        var jobList = jobs.ToList();
        var partIds = jobList
            .Where(j => j.PartId.HasValue)
            .Select(j => j.PartId!.Value)
            .Distinct()
            .ToList();
        if (partIds.Count == 0)
            return [];

        var steps = await db.Operations
            .AsNoTracking()
            .Where(o => partIds.Contains(o.PartId))
            .Select(o => new
            {
                o.Id,
                o.PartId,
                o.StepNumber,
                o.Title,
                o.WorkCenterId,
                WorkCenterName = o.WorkCenter != null ? o.WorkCenter.Name : null,
            })
            .ToListAsync(ct);

        var firstStepByPart = steps
            .GroupBy(o => o.PartId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(o => o.StepNumber).ThenBy(o => o.Id).First());

        var result = new Dictionary<int, KioskNextOperationResponseModel>();
        foreach (var (jobId, partId) in jobList)
        {
            if (partId is int id && firstStepByPart.TryGetValue(id, out var step))
                result[jobId] = new KioskNextOperationResponseModel(
                    step.Id, step.StepNumber, step.Title, step.WorkCenterId, step.WorkCenterName);
        }
        return result;
    }
}
