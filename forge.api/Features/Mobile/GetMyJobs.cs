using System.Security.Claims;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record GetMyJobsQuery : IRequest<List<MyJobResponseModel>>;

/// <summary>
/// The phone's "my work orders" list: open jobs assigned to the caller, due
/// soonest first, each flagged overdue against the shop's local day and
/// whether the caller has a timer running on it.
/// </summary>
public class GetMyJobsHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<GetMyJobsQuery, List<MyJobResponseModel>>
{
    public async Task<List<MyJobResponseModel>> Handle(GetMyJobsQuery request, CancellationToken ct)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => j.AssigneeId == userId
                && !j.IsArchived
                && j.Disposition == null
                && j.CompletedDate == null)
            .OrderBy(j => j.DueDate == null)
            .ThenBy(j => j.DueDate)
            .ThenBy(j => j.JobNumber)
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.Title,
                PartNumber = j.Part != null ? j.Part.PartNumber : null,
                Quantity = j.JobParts.Any(jp => jp.PartId == j.PartId)
                    ? (decimal?)j.JobParts.Where(jp => jp.PartId == j.PartId).Sum(jp => jp.Quantity)
                    : j.SalesOrderLineId != null
                        ? j.SalesOrderLine!.Quantity
                        : null,
                j.DueDate,
                j.CurrentStageId,
                StageName = j.CurrentStage.Name,
            })
            .ToListAsync(ct);

        if (jobs.Count == 0)
            return [];

        var jobIds = jobs.Select(j => j.Id).ToList();
        var timedJobIds = (await db.TimeEntries.AsNoTracking()
                .Where(t => t.UserId == userId
                    && t.TimerStart != null
                    && t.TimerStop == null
                    && t.JobId != null
                    && jobIds.Contains(t.JobId.Value))
                .Select(t => t.JobId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var shopToday = ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, ct), clock.UtcNow);

        return jobs
            .Select(j => new MyJobResponseModel(
                j.Id, j.JobNumber, j.Title, j.PartNumber, j.Quantity, j.DueDate,
                j.CurrentStageId, j.StageName,
                ClockStateRules.IsOverdue(j.DueDate, shopToday),
                timedJobIds.Contains(j.Id)))
            .ToList();
    }
}
