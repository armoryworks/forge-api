using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetTimeEntriesQuery(int? UserId, int? JobId, DateOnly? From, DateOnly? To) : IRequest<List<TimeEntryResponseModel>>;

public class GetTimeEntriesHandler(ITimeTrackingRepository repo, IHttpContextAccessor http) : IRequestHandler<GetTimeEntriesQuery, List<TimeEntryResponseModel>>
{
    public async Task<List<TimeEntryResponseModel>> Handle(GetTimeEntriesQuery request, CancellationToken cancellationToken)
    {
        var user = http.HttpContext?.User;
        var jobWide = request.JobId.HasValue && request.UserId is null;
        var userId = jobWide
            ? null
            : CallerAccess.ScopeToSelf(user, request.UserId, "You can only view your own time entries.");
        var entries = await repo.GetTimeEntriesAsync(userId, request.JobId, request.From, request.To, cancellationToken);

        if (!jobWide || CallerAccess.IsSupervisor(user))
            return entries;

        var callerId = CallerAccess.UserId(user);
        return entries
            .Select(e => e.UserId == callerId ? e : e with { LaborCost = 0m, BurdenCost = 0m })
            .ToList();
    }
}
