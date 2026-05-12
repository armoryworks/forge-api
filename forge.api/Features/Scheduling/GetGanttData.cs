using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record GetGanttDataQuery(DateOnly From, DateOnly To) : IRequest<List<ScheduledOperationResponseModel>>;

public class GetGanttDataHandler(AppDbContext db) : IRequestHandler<GetGanttDataQuery, List<ScheduledOperationResponseModel>>
{
    public async Task<List<ScheduledOperationResponseModel>> Handle(GetGanttDataQuery request, CancellationToken cancellationToken)
    {
        var fromDto = new DateTimeOffset(request.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toDto = new DateTimeOffset(request.To.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        return await db.ScheduledOperations
            .AsNoTracking()
            .Include(so => so.Job)
            .Include(so => so.Operation)
            .Include(so => so.WorkCenter)
            .Where(so => so.Status != ScheduledOperationStatus.Cancelled
                && so.ScheduledStart <= toDto
                && so.ScheduledEnd >= fromDto)
            .OrderBy(so => so.ScheduledStart)
            .Select(so => new ScheduledOperationResponseModel(
                so.Id, so.JobId, so.Job.JobNumber, so.Job.Title,
                so.OperationId, so.Operation.Title,
                so.WorkCenterId, so.WorkCenter.Name,
                so.ScheduledStart, so.ScheduledEnd,
                so.SetupHours, so.RunHours, so.TotalHours,
                so.Status, so.SequenceNumber, so.IsLocked,
                so.Job.Priority.ToString(), so.Job.DueDate, null))
            .ToListAsync(cancellationToken);
    }
}
