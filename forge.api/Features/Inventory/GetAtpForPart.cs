using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetAtpForPartQuery(int PartId, decimal Quantity) : IRequest<AtpResult>;

public class GetAtpForPartHandler(IAtpService atpService) : IRequestHandler<GetAtpForPartQuery, AtpResult>
{
    public async Task<AtpResult> Handle(GetAtpForPartQuery request, CancellationToken cancellationToken)
    {
        return await atpService.CalculateAtpAsync(request.PartId, request.Quantity, cancellationToken);
    }
}
