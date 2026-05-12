using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record GetScheduleRunsQuery() : IRequest<List<ScheduleRunResponseModel>>;

public class GetScheduleRunsHandler(AppDbContext db) : IRequestHandler<GetScheduleRunsQuery, List<ScheduleRunResponseModel>>
{
    public async Task<List<ScheduleRunResponseModel>> Handle(GetScheduleRunsQuery request, CancellationToken cancellationToken)
    {
        return await db.ScheduleRuns
            .AsNoTracking()
            .OrderByDescending(r => r.RunDate)
            .Take(50)
            .Select(r => new ScheduleRunResponseModel(
                r.Id, r.RunDate, r.Direction, r.Status,
                r.OperationsScheduled, r.ConflictsDetected,
                r.CompletedAt, r.RunByUserId, r.ErrorMessage))
            .ToListAsync(cancellationToken);
    }
}
