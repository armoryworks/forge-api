using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record GetRoutingGraphQuery(int PartId) : IRequest<RoutingGraphResponseModel>;

public class GetRoutingGraphHandler(IPartRepository repo) : IRequestHandler<GetRoutingGraphQuery, RoutingGraphResponseModel>
{
    public async Task<RoutingGraphResponseModel> Handle(GetRoutingGraphQuery request, CancellationToken cancellationToken)
    {
        _ = await repo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var operations = await repo.GetOperationsAsync(request.PartId, cancellationToken);
        return new RoutingGraphResponseModel(RoutingGraphVersion.Of(operations), operations);
    }
}
