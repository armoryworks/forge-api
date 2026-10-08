using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public static class KioskWork
{
    private const JobOperationStatus Complete = JobOperationStatus.Complete;
    private const JobOperationStatus Skipped = JobOperationStatus.Skipped;
    private const JobOperationStatus InProgress = JobOperationStatus.InProgress;

    private static readonly Expression<Func<Job, bool>> OpenAtShopFloorRule = j => !j.IsArchived
        && j.CompletedDate == null
        && j.Disposition == null
        && j.CurrentStage.IsShopFloor;

    private static readonly Func<Job, bool> OpenAtShopFloorCheck = OpenAtShopFloorRule.Compile();

    public static bool IsOpenAtShopFloor(Job job) => OpenAtShopFloorCheck(job);

    public static async Task<IQueryable<Job>> ReadyToStartAsync(
        AppDbContext db, int? teamId, bool tracking, CancellationToken ct)
    {
        var ready = db.Jobs.Where(OpenAtShopFloorRule).Where(j => j.AssigneeId == null);
        if (teamId is not int team || !await db.WorkCenters.AnyAsync(w => w.TeamId == team, ct))
            return ready;

        return ready.Where(j => db.Operations
            .Where(o => o.PartId == j.PartId
                && !(tracking && db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == o.Id
                    && (r.Status == Complete || r.Status == Skipped))))
            .OrderBy(o => tracking
                && (db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == o.Id && r.Status == InProgress)
                    || db.TimeEntries.Any(t => t.JobId == j.Id && t.OperationId == o.Id
                        && t.TimerStart != null && t.TimerStop == null))
                ? 0 : 1)
            .ThenBy(o => o.StepNumber)
            .ThenBy(o => o.Id)
            .Select(o => o.WorkCenter != null ? o.WorkCenter.TeamId : null)
            .FirstOrDefault() == team);
    }

    public static async Task<Dictionary<int, KioskNextOperationResponseModel>> NextOperationsAsync(
        AppDbContext db, IEnumerable<int> jobIds, bool tracking, CancellationToken ct)
    {
        var ids = jobIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var rows = await db.Jobs
            .AsNoTracking()
            .Where(j => ids.Contains(j.Id) && j.PartId != null)
            .Select(j => new
            {
                j.Id,
                Next = db.Operations
                    .Where(o => o.PartId == j.PartId
                        && !(tracking && db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == o.Id
                            && (r.Status == Complete || r.Status == Skipped))))
                    .OrderBy(o => tracking
                        && (db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == o.Id && r.Status == InProgress)
                            || db.TimeEntries.Any(t => t.JobId == j.Id && t.OperationId == o.Id
                                && t.TimerStart != null && t.TimerStop == null))
                        ? 0 : 1)
                    .ThenBy(o => o.StepNumber)
                    .ThenBy(o => o.Id)
                    .Select(o => new
                    {
                        o.Id,
                        o.StepNumber,
                        o.Title,
                        o.WorkCenterId,
                        WorkCenterName = o.WorkCenter != null ? o.WorkCenter.Name : null,
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows
            .Where(r => r.Next != null)
            .ToDictionary(
                r => r.Id,
                r => new KioskNextOperationResponseModel(
                    r.Next!.Id, r.Next.StepNumber, r.Next.Title, r.Next.WorkCenterId, r.Next.WorkCenterName));
    }
}
