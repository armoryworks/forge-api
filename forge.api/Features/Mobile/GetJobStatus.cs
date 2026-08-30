using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record GetJobStatusQuery(int JobId) : IRequest<JobStatusResponseModel>;

/// <summary>
/// The phone's job card: identity, where it is, where it goes next (and
/// where it came from, for undo), and the last three timeline entries.
/// </summary>
public class GetJobStatusHandler(AppDbContext db, IMediator mediator, IClock clock)
    : IRequestHandler<GetJobStatusQuery, JobStatusResponseModel>
{
    public async Task<JobStatusResponseModel> Handle(GetJobStatusQuery request, CancellationToken ct)
    {
        var job = await mediator.Send(new GetJobByIdQuery(request.JobId), ct);

        var stages = await db.JobStages.AsNoTracking()
            .Where(s => s.TrackTypeId == job.TrackTypeId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        var index = stages.FindIndex(s => s.Id == job.CurrentStageId);
        var next = index >= 0 && index + 1 < stages.Count ? stages[index + 1] : null;
        var previous = index > 0 ? stages[index - 1] : null;

        var activity = (await mediator.Send(new GetJobActivityQuery(request.JobId), ct))
            .OrderByDescending(a => a.CreatedAt)
            .Take(3)
            .ToList();

        var now = clock.UtcNow;
        return new JobStatusResponseModel(
            job.Id, job.JobNumber, job.Title, job.CustomerName,
            job.CurrentStageId, job.StageName, job.StageColor,
            job.DueDate, job.DueDate is not null && job.DueDate < now && job.CompletedDate is null,
            next?.Id, next?.Name, previous?.Id, previous?.Name,
            job.RowVersion, activity);
    }
}
