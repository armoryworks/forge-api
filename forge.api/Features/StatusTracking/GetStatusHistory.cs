using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.StatusTracking;

public record GetStatusHistoryQuery(string EntityType, int EntityId) : IRequest<List<StatusEntryResponseModel>>;

public class GetStatusHistoryHandler(IStatusEntryRepository repository)
    : IRequestHandler<GetStatusHistoryQuery, List<StatusEntryResponseModel>>
{
    public Task<List<StatusEntryResponseModel>> Handle(
        GetStatusHistoryQuery request, CancellationToken cancellationToken)
        => repository.GetHistoryAsync(request.EntityType, request.EntityId, cancellationToken);
}
