using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Activity;

public record GetEntityActivityQuery(string EntityType, int EntityId) : IRequest<List<ActivityResponseModel>>;

public class GetEntityActivityHandler(IActivityLogRepository repo)
    : IRequestHandler<GetEntityActivityQuery, List<ActivityResponseModel>>
{
    public async Task<List<ActivityResponseModel>> Handle(GetEntityActivityQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetByEntityAsync(request.EntityType, request.EntityId, cancellationToken);
    }
}
