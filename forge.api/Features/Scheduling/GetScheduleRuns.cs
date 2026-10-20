using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record GetScheduleRunsQuery() : IRequest<List<ScheduleRunResponseModel>>;

public class GetScheduleRunsHandler(AppDbContext db) : IRequestHandler<GetScheduleRunsQuery, List<ScheduleRunResponseModel>>
{
    public async Task<List<ScheduleRunResponseModel>> Handle(GetScheduleRunsQuery request, CancellationToken cancellationToken)
    {
        var runs = await db.ScheduleRuns
            .AsNoTracking()
            .OrderByDescending(r => r.RunDate)
            .Take(50)
            .ToListAsync(cancellationToken);
        return runs.Select(SchedulingService.MapToResponse).ToList();
    }
}
