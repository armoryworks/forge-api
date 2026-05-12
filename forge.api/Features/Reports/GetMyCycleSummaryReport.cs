using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetMyCycleSummaryReportQuery : IRequest<List<MyCycleSummaryReportItem>>;

public class GetMyCycleSummaryReportHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<GetMyCycleSummaryReportQuery, List<MyCycleSummaryReportItem>>
{
    public async Task<List<MyCycleSummaryReportItem>> Handle(GetMyCycleSummaryReportQuery request, CancellationToken ct)
    {
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var cycles = await db.PlanningCycles
            .Include(c => c.Entries)
                .ThenInclude(e => e.Job)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync(ct);

        return cycles
            .Select(c =>
            {
                var myEntries = c.Entries.Where(e => e.Job.AssigneeId == userId).ToList();
                var total = myEntries.Count;
                var completed = myEntries.Count(e => e.CompletedAt.HasValue);
                var rolledOver = myEntries.Count(e => e.IsRolledOver);
                var rate = total > 0 ? Math.Round((decimal)completed / total * 100, 1) : 0;

                return new MyCycleSummaryReportItem(
                    c.Id, c.Name, c.StartDate, c.EndDate,
                    total, completed, rate, rolledOver);
            })
            .Where(r => r.TotalEntries > 0)
            .ToList();
    }
}
