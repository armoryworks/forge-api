using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetTimeEntriesQuery(int? UserId, int? JobId, DateOnly? From, DateOnly? To) : IRequest<List<TimeEntryResponseModel>>;

public class GetTimeEntriesHandler(ITimeTrackingRepository repo, IHttpContextAccessor http) : IRequestHandler<GetTimeEntriesQuery, List<TimeEntryResponseModel>>
{
    public Task<List<TimeEntryResponseModel>> Handle(GetTimeEntriesQuery request, CancellationToken cancellationToken)
    {
        var user = http.HttpContext?.User;
        var userId = request.JobId.HasValue && request.UserId is null
            ? null
            : CallerAccess.ScopeToSelf(user, request.UserId, "You can only view your own time entries.");
        return repo.GetTimeEntriesAsync(userId, request.JobId, request.From, request.To, cancellationToken);
    }
}
