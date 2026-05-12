using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record GetOperationsQuery(int PartId) : IRequest<List<OperationResponseModel>>;

public class GetOperationsHandler(IPartRepository repo) : IRequestHandler<GetOperationsQuery, List<OperationResponseModel>>
{
    public async Task<List<OperationResponseModel>> Handle(GetOperationsQuery request, CancellationToken cancellationToken)
    {
        _ = await repo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        return await repo.GetOperationsAsync(request.PartId, cancellationToken);
    }
}
