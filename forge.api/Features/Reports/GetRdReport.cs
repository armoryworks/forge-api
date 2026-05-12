using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetRdReportQuery(DateTimeOffset? Start, DateTimeOffset? End)
    : IRequest<List<RdReportItem>>;

public class GetRdReportHandler(AppDbContext db)
    : IRequestHandler<GetRdReportQuery, List<RdReportItem>>
{
    public async Task<List<RdReportItem>> Handle(GetRdReportQuery request, CancellationToken ct)
    {
        var query = db.Jobs
            .Include(j => j.TrackType)
            .Include(j => j.CurrentStage)
            .Where(j => j.TrackType.Name.Contains("R&D"));

        if (request.Start.HasValue)
            query = query.Where(j => j.CreatedAt >= request.Start.Value.UtcDateTime);
        if (request.End.HasValue)
            query = query.Where(j => j.CreatedAt <= request.End.Value.UtcDateTime);

        var jobs = await query.ToListAsync(ct);
        var jobIds = jobs.Select(j => j.Id).ToList();

        // Assignee names
        var assigneeIds = jobs.Where(j => j.AssigneeId.HasValue).Select(j => j.AssigneeId!.Value).Distinct().ToList();
        var users = assigneeIds.Count > 0
            ? await db.Users.Where(u => assigneeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct)
            : new Dictionary<int, string>();

        // Hours per job from time entries
        var hoursByJob = await db.TimeEntries
            .Where(t => t.JobId.HasValue && jobIds.Contains(t.JobId.Value))
            .GroupBy(t => t.JobId!.Value)
            .Select(g => new { JobId = g.Key, TotalMinutes = g.Sum(t => t.DurationMinutes) })
            .ToDictionaryAsync(g => g.JobId, g => g.TotalMinutes, ct);

        return jobs
            .Select(j => new RdReportItem(
                j.Id,
                j.JobNumber,
                j.Title,
                j.IterationCount,
                Math.Round((decimal)hoursByJob.GetValueOrDefault(j.Id, 0) / 60, 1),
                j.CurrentStage.Name,
                j.AssigneeId.HasValue ? users.GetValueOrDefault(j.AssigneeId.Value) : null,
                j.StartDate,
                j.CompletedDate))
            .OrderByDescending(r => r.TotalHours)
            .ToList();
    }
}
